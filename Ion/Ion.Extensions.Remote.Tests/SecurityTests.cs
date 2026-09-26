using System.Net;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Ion.Testing;
using Ion.Tests;

namespace Ion.Extensions.Remote.Tests;

[Trait(TestConstants.CATEGORY, TestConstants.INTEGRATION)]
public sealed class SecurityTests
{
	private const string SpawnRequest = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"world.spawn\",\"params\":{\"name\":\"Intruder\"}}";
	private const string InfoRequest = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"game.info\"}";

	[Fact]
	public void TheServerIsOffByDefault()
	{
		using var host = new IonTestHost();
		host.Start();
		Assert.Null(host.Services.GetService<RemoteServer>());
		Assert.DoesNotContain(host.Loop.Schedule!.Print().Split('\n'), line => line.Contains("RemoteSystem", StringComparison.Ordinal));
	}

	[Fact]
	public void RequestsWithoutATokenAreUnauthorized()
	{
		using var game = new RemoteGame();
		var (status, body) = game.Post(InfoRequest, token: null);
		Assert.Equal(HttpStatusCode.Unauthorized, status);
		Assert.Equal(RemoteErrorCodes.Unauthorized, RemoteGame.ErrorCode(body!));

		var (mutateStatus, mutateBody) = game.Post(SpawnRequest, token: null);
		Assert.Equal(HttpStatusCode.Unauthorized, mutateStatus);
		Assert.Equal(RemoteErrorCodes.Unauthorized, RemoteGame.ErrorCode(mutateBody!));
		Assert.Equal(2, game.World.Size);
	}

	[Fact]
	public void RequestsWithAnUnknownTokenAreUnauthorized()
	{
		using var game = new RemoteGame();
		var (status, body) = game.Post(SpawnRequest, token: "not-the-token");
		Assert.Equal(HttpStatusCode.Unauthorized, status);
		Assert.Equal(RemoteErrorCodes.Unauthorized, RemoteGame.ErrorCode(body!));
		Assert.Equal(2, game.World.Size);
	}

	[Fact]
	public void TheReadTokenCannotCallAnyMutateMethod()
	{
		using var game = new RemoteGame(mutations: true);
		var read = game.Server.ReadToken!;
		Assert.Equal(HttpStatusCode.OK, game.Post(InfoRequest, read).Status);

		var mutates = game.Server.Methods.Methods.Where(m => m.Access == RemoteAccess.Mutate).Select(m => m.Name).ToList();
		Assert.NotEmpty(mutates);
		foreach (var method in mutates)
		{
			var (status, body) = game.Post($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"{method}\",\"params\":{{}}}}", read);
			Assert.Equal(HttpStatusCode.Forbidden, status);
			Assert.Equal(RemoteErrorCodes.Forbidden, RemoteGame.ErrorCode(body!));
		}

		Assert.Equal(2, game.World.Size);
		Assert.False(game.Server.IsPaused);
		Assert.False(game.Host.IsExitRequested);
	}

	[Fact]
	public void WithoutAllowMutationsThereIsNoMutateToken()
	{
		using var game = new RemoteGame(mutations: false);
		Assert.Null(game.Server.MutateToken);
		var (status, body) = game.Post(SpawnRequest, game.Server.ReadToken);
		Assert.Equal(HttpStatusCode.Forbidden, status);
		Assert.Contains("--remote-allow-mutations", body!["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

		var file = JsonNode.Parse(File.ReadAllText(game.Server.TokenFilePath!))!;
		Assert.Null(file["mutateToken"]);
		Assert.False(file["allowMutations"]!.GetValue<bool>());
	}

	[Fact]
	public void NonLoopbackBindIsRefusedUnlessExplicitlyAllowed()
	{
		using var refused = new RemoteGame(configure: host => host.WithConfiguration("Ion:Remote:Bind", "0.0.0.0"), start: false);
		var error = Assert.Throws<RemoteSecurityException>(() => refused.Host.Start());
		Assert.Contains("AllowNonLoopback", error.Message, StringComparison.Ordinal);

		using var allowed = new RemoteGame(configure: host => host
			.WithConfiguration("Ion:Remote:Bind", "0.0.0.0")
			.WithConfiguration("Ion:Remote:AllowNonLoopback", "true"), start: false);
		allowed.Host.Start();
		Assert.True(allowed.Host.Get<RemoteServer>().IsStarted);
	}

	[Fact]
	public void LoopbackIsTheDefaultBind()
	{
		Assert.Equal("127.0.0.1", new RemoteOptions().Bind);
		Assert.False(new RemoteOptions().Enabled);
		Assert.False(new RemoteOptions().AllowMutations);
		Assert.True(IPAddress.IsLoopback(RemoteServer.ResolveBind("localhost")));
	}

	[Fact]
	public void BrowserOriginsAndForeignHostsAreRefused()
	{
		using var game = new RemoteGame();
		var (originStatus, originBody) = game.Post(InfoRequest, game.Server.ReadToken, r => r.Headers.Add("Origin", "https://evil.example"));
		Assert.Equal(HttpStatusCode.Forbidden, originStatus);
		Assert.Equal(RemoteErrorCodes.Forbidden, RemoteGame.ErrorCode(originBody!));

		var (hostStatus, _) = game.Post(InfoRequest, game.Server.ReadToken, r => r.Headers.Host = "evil.example");
		Assert.Equal(HttpStatusCode.Forbidden, hostStatus);

		var (localStatus, _) = game.Post(InfoRequest, game.Server.ReadToken, r => r.Headers.Host = $"localhost:{game.Server.Port}");
		Assert.Equal(HttpStatusCode.OK, localStatus);
	}

	[Fact]
	public void TheTokenFileIsOwnerOnlyAndHasTheEndpoint()
	{
		using var game = new RemoteGame();
		var path = game.Server.TokenFilePath!;
		Assert.StartsWith(game.RunDirectory, path, StringComparison.Ordinal);
		if (!OperatingSystem.IsWindows())
		{
			Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
		}

		var info = JsonSerializer.Deserialize(File.ReadAllText(path), RemoteJsonContext.Default.RemoteEndpointInfo)!;
		Assert.Equal($"http://127.0.0.1:{game.Server.Port}/", info.Url);
		Assert.Equal(game.Server.ReadToken, info.ReadToken);
		Assert.Equal(game.Server.MutateToken, info.MutateToken);
		Assert.Equal(Environment.ProcessId, info.Pid);

		game.Dispose();
		Assert.False(File.Exists(path));
	}

	[Fact]
	public void TokensAreRandomPerRun()
	{
		using var a = new RemoteGame();
		using var b = new RemoteGame();
		Assert.NotEqual(a.Server.ReadToken, b.Server.ReadToken);
		Assert.NotEqual(a.Server.MutateToken, a.Server.ReadToken);
		Assert.True(a.Server.ReadToken!.Length >= 40);
	}

	[Fact]
	public void AReplayedMutationIdAppliesOnce()
	{
		using var game = new RemoteGame();
		var request = new JsonObject { ["name"] = "Once", ["components"] = new JsonObject { ["Hidden"] = new JsonObject() } };
		var first = game.Call("world.spawn", request, id: "spawn-1");
		var second = game.Call("world.spawn", request, id: "spawn-1");
		Assert.Null(first["error"]);
		Assert.Equal(RemoteGame.Compact(first), RemoteGame.Compact(second));
		Assert.Equal(3, game.World.Size);

		// The same id with different parameters is a different request.
		var other = game.Call("world.spawn", new JsonObject { ["name"] = "Twice" }, id: "spawn-1");
		Assert.Null(other["error"]);
		Assert.Equal(4, game.World.Size);

		// Input is a mutation too: a retried input.send does not press the key twice.
		var input = new JsonObject { ["events"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "x" }) };
		Assert.Equal(1, game.Call("input.send", input, id: 77)["result"]!["queued"]!.GetValue<int>());
		Assert.Equal(1, game.Call("input.send", input, id: 77)["result"]!["queued"]!.GetValue<int>());
		game.Host.Step(2);
		Assert.Equal("text x", Assert.Single(game.Probe.Seen, s => s.StartsWith("text", StringComparison.Ordinal)));
	}

	[Fact]
	public void MutationsAreRejectedWhileASceneLoads()
	{
		using var game = new RemoteGame(scenes: true);
		game.Host.Step();

		// The probe emits ChangeSceneEvent in every Update while this is set; the remote step at the end of each such frame
		// sees a scene change pending.
		game.Probe.EmitSceneChange = true;
		game.Host.Step();
		var response = game.Call("world.spawn", new JsonObject { ["name"] = "DuringLoad" }, id: "load-1");
		Assert.Equal(RemoteErrorCodes.SceneLoading, RemoteGame.ErrorCode(response));
		Assert.True(game.Result("game.info")!["sceneLoading"]!.GetValue<bool>());

		// Reads still work, and the same id applies once the scene has loaded.
		game.Probe.EmitSceneChange = false;
		game.Host.Step(2);
		var retry = game.Call("world.spawn", new JsonObject { ["name"] = "DuringLoad" }, id: "load-1");
		Assert.Null(retry["error"]);
	}

	[Fact]
	public void TheModuleCanBeCompiledOut()
	{
		// The feature switch defaults to on when unset; the build targets turn it off for Release unless IonRemote=true.
		Assert.True(RemoteFeature.IsSupported);
		Assert.Equal("Ion.Remote.IsSupported", RemoteFeature.SwitchName);
	}

	[Fact]
	public void CommandLineSwitchesMapToConfiguration()
	{
		var args = IonCommandLine.Normalize(["--headless", "--remote-allow-mutations", "--Ion:Seed=4"]);
		Assert.Equal(["--Ion:Headless=true", "--Ion:Remote:Enabled=true", "--Ion:Remote:AllowMutations=true", "--Ion:Seed=4"], args);
		Assert.Equal(["--Ion:Remote:Enabled=true", "--Ion:Remote:Transport=Stdio"], IonCommandLine.Normalize(["--remote-stdio"]));
	}
}
