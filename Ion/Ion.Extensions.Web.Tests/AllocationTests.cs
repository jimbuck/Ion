using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Web.Tests;

/// <summary>
/// The request path allocates nothing in steady state for small requests: on the connection thread (parse, route,
/// queue, wait, write) and on the game thread (the web step: dequeue, bind, call, write the result, signal). The game
/// thread is the test thread here, running only the web step, so the measure is the module's alone.
/// </summary>
[Trait(CATEGORY, INTEGRATION)]
public class AllocationTests
{
	private const int Warmup = 300;
	private const int Measured = 500;

	private static void Exchange(NetworkStream stream, byte[] request, byte[] buffer)
	{
		stream.Write(request);
		var read = 0;
		while (true)
		{
			read += stream.Read(buffer, read, buffer.Length - read);
			var text = buffer.AsSpan(0, read);
			var end = text.IndexOf("\r\n\r\n"u8);
			if (end < 0) continue;
			var lengthAt = text.IndexOf("Content-Length: "u8);
			var lengthEnd = text[(lengthAt + 16)..].IndexOf("\r\n"u8);
			var length = int.Parse(Encoding.ASCII.GetString(text.Slice(lengthAt + 16, lengthEnd)), System.Globalization.CultureInfo.InvariantCulture);
			if (read >= end + 4 + length) return;
		}
	}

	/// <summary>Runs the web step on this thread until <paramref name="client"/> finishes, summing its allocations once <paramref name="measuring"/> is set.</summary>
	private static long PumpWebStep(WebServer server, Thread client, Func<bool> measuring)
	{
		long allocated = 0;
		while (client.IsAlive)
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			server.ProcessFrame();
			var delta = GC.GetAllocatedBytesForCurrentThread() - before;
			if (measuring()) allocated += delta;
			Thread.SpinWait(20);
		}

		server.ProcessFrame();
		return allocated;
	}

	[Theory]
	[InlineData("GET /number HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n")]
	[InlineData("POST /score/add?amount=1 HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 0\r\n\r\n")]
	[InlineData("GET /score HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n")]
	public void SmallRequestsAllocateNothingInSteadyState(string text)
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:RateLimit"] = "0" }, background: false);
		var server = game.Server;
		var request = Encoding.ASCII.GetBytes(text);
		long connectionBefore = 0, connectionAfter = 0;
		var measuring = false;
		Exception? failure = null;
		var client = new Thread(() =>
		{
			try
			{
				var buffer = new byte[4096];
				using var tcp = new TcpClient("127.0.0.1", server.Port) { NoDelay = true };
				var stream = tcp.GetStream();
				for (var i = 0; i < Warmup; i++) Exchange(stream, request, buffer);
				connectionBefore = server.LastServeThreadAllocatedBytes;
				Volatile.Write(ref measuring, true);
				for (var i = 0; i < Measured; i++) Exchange(stream, request, buffer);
				connectionAfter = server.LastServeThreadAllocatedBytes;
				Volatile.Write(ref measuring, false);
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		});
		client.Start();

		var gameThread = PumpWebStep(server, client, () => Volatile.Read(ref measuring));
		Assert.Null(failure);
		Assert.Equal(0, connectionAfter - connectionBefore);
		Assert.Equal(0, gameThread);
	}

	[Fact]
	public void WebSocketMessagesAllocateNothingOnTheGameThread()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:RateLimit"] = "0" }, background: false);
		var server = game.Server;
		var measuring = false;
		Exception? failure = null;
		var client = new Thread(() =>
		{
			try
			{
				using var socket = new ClientWebSocket();
				socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/count"), CancellationToken.None).GetAwaiter().GetResult();
				var payload = "{\"x\":0.5}"u8.ToArray();
				var buffer = new byte[256];
				void RoundTrip()
				{
					socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
					socket.ReceiveAsync(buffer, CancellationToken.None).GetAwaiter().GetResult();
				}

				for (var i = 0; i < Warmup; i++) RoundTrip();
				Volatile.Write(ref measuring, true);
				for (var i = 0; i < Measured; i++) RoundTrip();
				Volatile.Write(ref measuring, false);
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		});
		client.Start();

		var gameThread = PumpWebStep(server, client, () => Volatile.Read(ref measuring));
		Assert.Null(failure);
		Assert.Equal(Warmup + Measured, game.System.Counted);
		Assert.Equal(0, gameThread);
	}
}
