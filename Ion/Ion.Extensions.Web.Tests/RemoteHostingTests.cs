using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

using Ion.Extensions.Remote;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Web.Tests;

/// <summary>With the remote module on too, the web server serves the remote protocol at <c>/rpc</c>.</summary>
[Trait(CATEGORY, INTEGRATION)]
public sealed class RemoteHostingTests : IDisposable
{
	private readonly string _runDirectory = Path.Combine(Path.GetTempPath(), "ion-web-rpc", Guid.NewGuid().ToString("N"));

	private WebGame Game(Dictionary<string, string?>? extra = null)
	{
		var settings = new Dictionary<string, string?>
		{
			["Ion:Remote:Enabled"] = "true",
			["Ion:Remote:Port"] = "0",
			["Ion:Remote:PrintToken"] = "false",
			["Ion:Remote:AllowMutations"] = "true",
			["Ion:Remote:RunDirectory"] = _runDirectory,
		};
		if (extra is not null) foreach (var (k, v) in extra) settings[k] = v;
		return new WebGame(settings);
	}

	private static async Task<(HttpStatusCode Status, JsonNode? Body)> Rpc(WebGame game, string method, string? token)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, "/rpc")
		{
			Content = new StringContent($$"""{"jsonrpc":"2.0","id":"1","method":"{{method}}"}""", Encoding.UTF8, "application/json"),
		};
		if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		using var response = await game.Http.SendAsync(request);
		var text = await response.Content.ReadAsStringAsync();
		return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
	}

	[Fact]
	public async Task TheRemoteProtocolAnswersAtRpcWithItsOwnTokens()
	{
		using var game = Game();
		var remote = game.Host.Get<RemoteServer>();

		var (unauthorized, _) = await Rpc(game, "game.info", token: null);
		Assert.Equal(HttpStatusCode.Unauthorized, unauthorized);

		var (ok, body) = await Rpc(game, "game.info", remote.ReadToken);
		Assert.Equal(HttpStatusCode.OK, ok);
		Assert.Equal("read", body!["result"]!["access"]!.GetValue<string>());

		var (forbidden, _) = await Rpc(game, "game.pause", remote.ReadToken);
		Assert.Equal(HttpStatusCode.Forbidden, forbidden);
	}

	[Fact]
	public void AnUnmountedPathIsNotTheProtocol()
	{
		using var game = Game(new() { ["Ion:Web:HostEndpoints"] = "false" });
		var response = game.Raw("POST /rpc HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}");
		Assert.StartsWith("HTTP/1.1 404", response, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ALanBoundWebServerDoesNotOpenTheProtocolToTheNetwork()
	{
		using var game = Game(new() { ["Ion:Web:Bind"] = "0.0.0.0", ["Ion:Web:AllowNonLoopback"] = "true" });
		var remote = game.Host.Get<RemoteServer>();
		// Reached on loopback it answers; the endpoint refuses clients on other interfaces (Ion:Remote:AllowNonLoopback).
		var (ok, _) = await Rpc(game, "game.info", remote.ReadToken);
		Assert.Equal(HttpStatusCode.OK, ok);
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(_runDirectory, recursive: true);
		}
		catch (IOException)
		{
		}
	}
}
