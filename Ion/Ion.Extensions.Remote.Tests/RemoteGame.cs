using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion.Extensions.Ecs;
using Ion.Extensions.Scenes;
using Ion.Testing;

namespace Ion.Extensions.Remote.Tests;

/// <summary>An event type exposed through events.tail in the tests.</summary>
public record struct PingEvent(int Value);

[JsonSourceGenerationOptions(IncludeFields = true)]
[JsonSerializable(typeof(PingEvent))]
[JsonSerializable(typeof(int))]
internal sealed partial class TestJsonContext : JsonSerializerContext
{
}

/// <summary>Records what the game sees, for input and event tests.</summary>
public sealed class ProbeSystem(IInputState input, IEvents events, Microsoft.Extensions.Logging.ILogger<ProbeSystem> logger)
{
	public List<string> Seen { get; } = [];

	public int Score { get; set; }

	public bool EmitSceneChange { get; set; }

	[Update]
	public void Observe(GameTime dt)
	{
		if (input.Pressed(Key.Space)) Seen.Add("space");
		if (input.Pressed(MouseButton.Left)) Seen.Add($"click {input.MousePosition.X.ToString(CultureInfo.InvariantCulture)},{input.MousePosition.Y.ToString(CultureInfo.InvariantCulture)}");
		if (input.Text.Length > 0) Seen.Add($"text {input.Text.ToString()}");
		if (input.Gamepads.Count > 0 && input.Gamepad(0).Pressed(GamepadButton.A)) Seen.Add("pad A");
		if (dt.Frame == 2)
		{
			events.Emit(new PingEvent(42));
			logger.LogWarning("Probe warning at frame {Frame}", dt.Frame);
		}

		if (EmitSceneChange) events.EmitChangeScene(2);
	}
}

/// <summary>
/// A headless game with the remote server on (HTTP on a free loopback port, tokens in a temporary run directory), an ECS
/// world with two named entities, scenes 1 and 2, and a <see cref="ProbeSystem"/>. Remote calls pump frames until the
/// answer arrives, since the game thread (the test thread) applies requests at the end of each frame.
/// </summary>
public sealed class RemoteGame : IDisposable
{
	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

	public RemoteGame(bool mutations = true, Action<IonTestHost>? configure = null, bool scenes = false, bool start = true)
	{
		RunDirectory = Path.Combine(Path.GetTempPath(), "ion-remote-tests", Guid.NewGuid().ToString("N"));
		Host = new IonTestHost()
			.WithConfiguration(new Dictionary<string, string?>
			{
				["Ion:Remote:Enabled"] = "true",
				["Ion:Remote:Port"] = "0",
				["Ion:Remote:PrintToken"] = "false",
				["Ion:Remote:AllowMutations"] = mutations ? "true" : "false",
				["Ion:Remote:RunDirectory"] = RunDirectory,
				["Ion:Title"] = "RemoteTestGame",
			})
			.Configure(services =>
			{
				services.AddEcs().AddEcsSerialization();
				services.AddSingleton<ProbeSystem>();
				services.AddRemoteEvent(TestJsonContext.Default.PingEvent);
				services.AddRemoteResource("Test.Score", "The probe's score.", TestJsonContext.Default.Int32,
					static sp => sp.GetRequiredService<ProbeSystem>().Score,
					static (sp, value) => sp.GetRequiredService<ProbeSystem>().Score = value);
			})
			.ConfigureApp(app =>
			{
				app.UseEcs().UseSystem<ProbeSystem>();
				app.Init((GameTime dt, World world) =>
				{
					world.Create(new EntityName("Ball"), new Transform2D(new Vector2(10, 20)));
					world.Create(new EntityName("Paddle"), new Transform2D(new Vector2(100, 400)), new Hidden());
				});
				if (scenes) app.UseScene(1, _ => { }).UseScene(2, _ => { });
			});

		configure?.Invoke(Host);
		if (start)
		{
			Host.Start();
			Server = Host.Get<RemoteServer>();
		}
	}

	public IonTestHost Host { get; }

	public RemoteServer Server { get; private set; } = null!;

	public string RunDirectory { get; }

	public Uri Url => new($"http://127.0.0.1:{Server.Port}/");

	public ProbeSystem Probe => Host.Get<ProbeSystem>();

	public World World => Host.Get<EcsWorlds>().Worlds[^1];

	private Thread? _background;
	private volatile bool _stop;

	/// <summary>Runs the game loop on a background thread from now on (calls then no longer pump frames).</summary>
	public RemoteGame RunInBackground()
	{
		_background = new Thread(() =>
		{
			while (!_stop && !Host.IsExitRequested)
			{
				Host.Step();
				Thread.Sleep(1);
			}
		}) { IsBackground = true, Name = "RemoteGame loop" };
		_background.Start();
		return this;
	}

	/// <summary>Whether the background loop has ended (the game exited).</summary>
	public bool BackgroundLoopEnded => _background is { IsAlive: false };

	/// <summary>Runs <paramref name="work"/> on a pool thread while stepping frames until it completes.</summary>
	public T Pump<T>(Func<T> work, int maxFrames = 5000)
	{
		if (_background is not null) return work();
		var task = Task.Run(work);
		for (var i = 0; i < maxFrames && !task.IsCompleted; i++)
		{
			Host.Step();
			task.Wait(2);
		}

		if (!task.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The remote call did not complete.");
		return task.Result;
	}

	/// <summary>POSTs a raw body; returns the status and the JSON body (null for an empty body).</summary>
	public (HttpStatusCode Status, JsonObject? Body) Post(string body, string? token, Action<HttpRequestMessage>? customize = null) => Pump(() =>
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
		if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		customize?.Invoke(request);
		using var response = Http.Send(request);
		var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
		return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text)!.AsObject());
	});

	/// <summary>Calls <paramref name="method"/> with the mutate token (or <paramref name="token"/>); returns the whole response.</summary>
	public JsonObject Call(string method, JsonObject? parameters = null, string? token = null, JsonNode? id = null)
	{
		var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone() ?? JsonValue.Create(Interlocked.Increment(ref _nextId)), ["method"] = method };
		if (parameters is not null) request["params"] = parameters.DeepClone();
		var (_, body) = Post(request.ToJsonString(), token ?? Server.MutateToken ?? Server.ReadToken);
		return body!;
	}

	/// <summary>Calls <paramref name="method"/> and returns its result, failing on an error response.</summary>
	public JsonNode? Result(string method, JsonObject? parameters = null)
	{
		var response = Call(method, parameters);
		if (response["error"] is { } error) Assert.Fail($"{method} failed: {error.ToJsonString()}");
		return response["result"];
	}

	/// <summary>The error code of a response.</summary>
	public static int? ErrorCode(JsonObject response) => response["error"]?["code"]?.GetValue<int>();

	private static int _nextId;

	public void Dispose()
	{
		if (_background is { } thread)
		{
			_stop = true;
			Server.Resume();
			thread.Join(TimeSpan.FromSeconds(10));
		}

		Host.Dispose();
		try
		{
			Directory.Delete(RunDirectory, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	internal static string Compact(JsonNode? node) => node?.ToJsonString(new JsonSerializerOptions()) ?? "null";
}
