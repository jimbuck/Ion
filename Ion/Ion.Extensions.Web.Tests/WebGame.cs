using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Ion.Testing;

namespace Ion.Extensions.Web.Tests;

public record struct ScoreInfo(int Score, long Frame);

public sealed class Player
{
	public string? Name { get; set; }

	public int Level { get; set; }
}

[JsonSerializable(typeof(ScoreInfo))]
[JsonSerializable(typeof(Player))]
internal sealed partial class TestJson : JsonSerializerContext;

/// <summary>One handled request or message, as the game thread saw it.</summary>
public readonly record struct Handled(string What, long Frame, int Thread, bool InWebStep);

/// <summary>
/// The endpoints of the tests: ordinary methods on a system, routed by the generated table of this assembly.
/// </summary>
[WebJson(typeof(TestJson))]
public sealed class TestWebSystem(GameLoopContext loop)
{
	private readonly Lock _lock = new();

	public int Score;

	public int GameThread;

	public bool InWebStep;

	public List<Handled> Log { get; } = [];

	public List<string> Messages { get; } = [];

	public int Connections;

	public int Disconnections;

	[Update]
	public void Frame(GameTime dt) => GameThread = Environment.CurrentManagedThreadId;

	// Brackets around the web step, to check that the handlers run inside it.
	[Last(Order = StageOrder.Web - 1)]
	public void BeforeWeb(GameTime dt) => InWebStep = true;

	[Last(Order = StageOrder.Web + 1)]
	public void AfterWeb(GameTime dt) => InWebStep = false;

	private void Record(string what)
	{
		lock (_lock) Log.Add(new Handled(what, loop.Frame, Environment.CurrentManagedThreadId, InWebStep));
	}

	[Http("GET", "/score")]
	public ScoreInfo GetScore() => new(Score, loop.Frame);

	[Http("GET", "/number")]
	public int Number() => Score;

	[Http("POST", "/score/add")]
	public void Add(int amount) => Score += amount;

	[Http("GET", "/echo/{text}")]
	public string Echo(string text) => text;

	[Http("GET", "/files/{*path}")]
	public string File(string path) => "path:" + path;

	[Http("GET", "/query")]
	public string Query(int count, bool flag = false, string? name = null, double scale = 1.5) =>
		FormattableString.Invariant($"{count}|{flag}|{name ?? "null"}|{scale}");

	[Http("PUT", "/players/{id}/name")]
	public string Rename(int id, [FromBody] string name) => FormattableString.Invariant($"{id}={name}");

	[Http("POST", "/players")]
	public Player LevelUp([FromBody] Player player)
	{
		player.Level++;
		return player;
	}

	[Http("GET", "/fail")]
	public int Fail() => throw new InvalidOperationException("Handler exploded.");

	[Http("GET", "/custom")]
	public void Custom(ref WebResponse response, in WebRequest request)
	{
		response.Header("X-Test", "yes");
		response.Text("custom " + request.Query("q"), 202);
	}

	[Http("POST", "/order")]
	public void Order(int n) => Record("http " + n.ToString(System.Globalization.CultureInfo.InvariantCulture));

	[Http("GET", "/bytes")]
	public void Bytes(WebResponse response) => response.Bytes("abc"u8, "application/octet-stream");

	[WebSocket("/events", Access = WebAccess.Read)]
	public void Events(in WebSocketMessage message)
	{
		if (message.Kind == WebSocketMessageKind.Connected) Connections++;
		if (message.Kind == WebSocketMessageKind.Disconnected) Disconnections++;
	}

	[WebSocket("/echo", Access = WebAccess.Read)]
	public void EchoSocket(in WebSocketMessage message)
	{
		if (!message.IsMessage) return;
		var text = message.Text;
		Record("ws " + text);
		lock (_lock) Messages.Add(text);
		message.Client.Send(message.Data);
	}

	[WebSocket("/control")]
	public void Control(in WebSocketMessage message)
	{
		if (message.IsMessage) lock (_lock) Messages.Add("control " + message.Text);
	}

	public int Counted;

	/// <summary>Counts messages and echoes them without allocating.</summary>
	[WebSocket("/count", Access = WebAccess.Read)]
	public void CountSocket(in WebSocketMessage message)
	{
		if (!message.IsMessage) return;
		Counted++;
		message.Client.Send(message.Data);
	}

	public Handled[] Snapshot()
	{
		lock (_lock) return [.. Log];
	}
}

/// <summary>
/// A headless game with the web server on (a free loopback port unless configured), <see cref="TestWebSystem"/>, and the
/// game loop on a background thread (the game thread), which records its allocations per frame.
/// </summary>
public sealed class WebGame : IDisposable
{
	private Thread? _loop;
	private volatile bool _stop;
	private Exception? _loopError;

	public WebGame(IDictionary<string, string?>? settings = null, bool start = true, bool background = true, Action<IServiceCollection>? services = null)
	{
		var config = new Dictionary<string, string?>
		{
			["Ion:Web:Enabled"] = "true",
			["Ion:Web:Port"] = "0",
			["Ion:Web:PrintUrl"] = "false",
		};
		if (settings is not null) foreach (var (k, v) in settings) config[k] = v;

		Host = new IonTestHost()
			.WithConfiguration(config)
			.UseGame(b =>
			{
				b.Services.AddIon(b.Configuration);
				b.Services.AddWeb(b.Configuration);
				b.Services.AddSingleton<TestWebSystem>();
				services?.Invoke(b.Services);
			}, a => a.UseIon().UseWeb().UseSystem<TestWebSystem>());

		if (!start) return;
		Host.Start();
		Server = Host.Get<WebServer>();
		Http = new HttpClient { BaseAddress = new Uri(Server.BaseUrl!), Timeout = TimeSpan.FromSeconds(30) };
		if (background) RunInBackground();
	}

	public IonTestHost Host { get; }

	public WebServer Server { get; } = null!;

	public HttpClient Http { get; } = null!;

	public TestWebSystem System => Host.Get<TestWebSystem>();

	public int Port => Server.Port;

	/// <summary>The game thread's allocated bytes, read at the end of its last frame.</summary>
	public long GameThreadAllocated => Interlocked.Read(ref _allocated);

	/// <summary>The number of frames the background loop ran.</summary>
	public long Frames => Interlocked.Read(ref _frames);

	private long _allocated;
	private long _frames;

	public void RunInBackground()
	{
		_loop = new Thread(() =>
		{
			try
			{
				while (!_stop && !Host.IsExitRequested)
				{
					Host.Step();
					Interlocked.Exchange(ref _allocated, GC.GetAllocatedBytesForCurrentThread());
					Interlocked.Increment(ref _frames);
					Thread.Sleep(1);
				}
			}
			catch (Exception ex)
			{
				_loopError = ex;
			}
		}) { IsBackground = true, Name = "WebGame loop" };
		_loop.Start();
	}

	/// <summary>Waits until the loop ran <paramref name="count"/> more frames.</summary>
	public void WaitFrames(int count)
	{
		var target = Frames + count;
		var deadline = DateTime.UtcNow.AddSeconds(20);
		while (Frames < target)
		{
			if (_loopError is not null) throw new InvalidOperationException("The game loop failed.", _loopError);
			if (DateTime.UtcNow > deadline) throw new TimeoutException("The game loop stalled.");
			Thread.Sleep(1);
		}
	}

	/// <summary>Sends raw bytes on a new connection and returns everything the server sends until it closes (or the timeout).</summary>
	public string Raw(string request, int timeoutMs = 5000) => Raw(Encoding.ASCII.GetBytes(request), timeoutMs);

	public string Raw(byte[] request, int timeoutMs = 5000)
	{
		using var client = new TcpClient();
		client.Connect("127.0.0.1", Port);
		client.ReceiveTimeout = timeoutMs;
		var stream = client.GetStream();
		stream.Write(request);
		return ReadAll(stream);
	}

	public static string ReadAll(NetworkStream stream)
	{
		var buffer = new byte[65536];
		var result = new MemoryStream();
		try
		{
			int read;
			while ((read = stream.Read(buffer)) > 0) result.Write(buffer, 0, read);
		}
		catch (IOException)
		{
			// Timed out or reset: return what arrived.
		}

		return Encoding.UTF8.GetString(result.ToArray());
	}

	public HttpRequestMessage Request(HttpMethod method, string path, string? token = null, HttpContent? content = null)
	{
		var request = new HttpRequestMessage(method, path) { Content = content };
		if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		return request;
	}

	public void Dispose()
	{
		_stop = true;
		_loop?.Join(TimeSpan.FromSeconds(10));
		Http?.Dispose();
		Host.Dispose();
		if (_loopError is not null) throw new InvalidOperationException("The game loop failed.", _loopError);
	}
}
