using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

using BenchmarkDotNet.Attributes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Ion.Extensions.Web;

namespace Ion.Benchmarks;

/// <summary>
/// The web module on loopback (Ion.Extensions.Web), with the game thread replaced by a thread that runs the web step
/// continuously, so the numbers are the server's own cost rather than the frame time a real game adds (a request waits
/// for the end of the frame it arrives in: up to one frame at 60 fps).
/// <list type="bullet">
/// <item><c>Request_RoundTrip</c>: one keep-alive client sends <c>GET /number</c> and reads the answer: parse, route,
/// queue into the game thread, handler, JSON number, back to the connection thread, response.</item>
/// <item><c>WebSocket_Push</c>: the game thread broadcasts 1,000 small messages on a channel and the benchmark waits
/// until every client has received them all (per-client copies, writer threads, framing).</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class WebBenchmarks
{
	private const int PushCount = 1000;

	private WebServer _server = null!;
	private ServiceProvider _services = null!;
	private Thread _gameThread = null!;
	private volatile bool _stop;
	private TcpClient _client = null!;
	private NetworkStream _stream = null!;
	private readonly byte[] _request = "GET /number HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"u8.ToArray();
	private readonly byte[] _buffer = new byte[4096];
	private readonly List<ClientWebSocket> _sockets = [];
	private readonly List<Task> _receivers = [];
	private long _received;
	private WebSocketChannel _channel = null!;
	private readonly byte[] _message = Encoding.UTF8.GetBytes("{\"score\":12,\"misses\":3}");

	/// <summary>The number of WebSocket clients receiving the pushes.</summary>
	[Params(1, 4)]
	public int Clients { get; set; }

	private sealed class Target
	{
		public int Value = 42;
	}

	[GlobalSetup]
	public void Setup()
	{
		var target = new Target();
		var table = new WebRouteTable("Benchmarks",
			[new WebRoute("GET", "/number", typeof(Target), _ => target, static (object t, in WebRequest _, WebResponse response) => response.JsonNumber(((Target)t).Value))],
			[new WebSocketRoute("/push", typeof(Target), _ => target, static (object _, in WebSocketMessage _) => { }, WebAccess.Read)]);
		_services = new ServiceCollection().AddSingleton(table).BuildServiceProvider();
		_server = new WebServer(_services, Options.Create(new WebOptions { Enabled = true, Port = 0, PrintUrl = false, RateLimit = 0, UseGeneratedRoutes = false, HostEndpoints = false }));
		_server.Start();
		_gameThread = new Thread(() =>
		{
			while (!_stop) _server.ProcessFrame();
		}) { IsBackground = true, Name = "Benchmark web step" };
		_gameThread.Start();

		_client = new TcpClient("127.0.0.1", _server.Port) { NoDelay = true };
		_stream = _client.GetStream();
		for (var i = 0; i < 1000; i++) Request_RoundTrip();

		_channel = _server.Channel("/push");
		for (var i = 0; i < Clients; i++)
		{
			var socket = new ClientWebSocket();
			socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_server.Port}/push"), CancellationToken.None).GetAwaiter().GetResult();
			_sockets.Add(socket);
			_receivers.Add(Task.Run(async () =>
			{
				var buffer = new byte[256];
				while (socket.State == WebSocketState.Open)
				{
					var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
					if (result.MessageType == WebSocketMessageType.Close) return;
					if (result.EndOfMessage) Interlocked.Increment(ref _received);
				}
			}));
		}

		while (_channel.Count < Clients) Thread.Sleep(1);
		WebSocket_Push();
	}

	[Benchmark]
	public int Request_RoundTrip()
	{
		_stream.Write(_request);
		var read = 0;
		while (true)
		{
			read += _stream.Read(_buffer, read, _buffer.Length - read);
			// The answer ends with the two-digit body.
			if (read > 4 && _buffer[read - 1] == (byte)'2' && _buffer[read - 2] == (byte)'4') return read;
		}
	}

	[Benchmark(OperationsPerInvoke = PushCount)]
	public long WebSocket_Push()
	{
		var target = Interlocked.Read(ref _received) + (long)PushCount * Clients;
		for (var i = 0; i < PushCount; i++) _channel.Broadcast(_message);
		var spin = new SpinWait();
		while (Interlocked.Read(ref _received) < target) spin.SpinOnce(sleep1Threshold: -1);
		return target;
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_stop = true;
		_gameThread.Join();
		foreach (var socket in _sockets) socket.Abort();
		_client.Dispose();
		_server.Dispose();
		_services.Dispose();
	}
}
