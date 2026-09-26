using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Http;

/// <summary>Settings of an <see cref="HttpSocketListener"/>.</summary>
public sealed class HttpListenerSettings
{
	/// <summary>A name for threads and logs (for example <c>Ion remote</c> or <c>Ion web</c>).</summary>
	public string Name { get; init; } = "Ion HTTP";

	/// <summary>The most concurrent connections; one more is answered 503 and closed.</summary>
	public int MaxConnections { get; init; } = 8;

	/// <summary>The largest request head in bytes (larger is 431).</summary>
	public int MaxHeadBytes { get; init; } = 16 * 1024;

	/// <summary>How long a connection may stay silent while a request is expected, in milliseconds (0: forever).</summary>
	public int ReceiveTimeoutMs { get; init; } = 120_000;

	/// <summary>The content type of the 503 answer to one connection too many.</summary>
	public string BusyContentType { get; init; } = "text/plain";

	/// <summary>The body of the 503 answer to one connection too many.</summary>
	public byte[] BusyBody { get; init; } = "Too many connections."u8.ToArray();
}

/// <summary>
/// A blocking HTTP listener on <see cref="System.Net.Sockets"/>: one accept thread and one thread per connection, which
/// runs <c>serve</c> with the <see cref="HttpConnection"/> until it returns (then the connection is closed). No async, no
/// thread pool: the threads may block, the game thread never does.
/// </summary>
public sealed class HttpSocketListener : IDisposable
{
	private readonly TcpListener _listener;
	private readonly Thread _acceptThread;
	private readonly Action<HttpConnection> _serve;
	private readonly HttpListenerSettings _settings;
	private readonly ILogger? _logger;
	private readonly List<HttpConnection> _connections = [];
	private volatile bool _disposed;

	/// <summary>Binds <paramref name="address"/>:<paramref name="port"/> (0 picks a free port) and starts accepting.</summary>
	public HttpSocketListener(IPAddress address, int port, HttpListenerSettings settings, Action<HttpConnection> serve, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(address);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(serve);
		_settings = settings;
		_serve = serve;
		_logger = logger;
		Address = address;
		_listener = new TcpListener(address, port);
		_listener.Server.NoDelay = true;
		_listener.Start(backlog: 64);
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = settings.Name + " accept" };
		_acceptThread.Start();
	}

	/// <summary>The bound address.</summary>
	public IPAddress Address { get; }

	/// <summary>The bound port.</summary>
	public int Port { get; }

	/// <summary>Whether the bound address is a loopback address.</summary>
	public bool IsLoopback => IPAddress.IsLoopback(Address);

	/// <summary>The number of open connections.</summary>
	public int ConnectionCount
	{
		get
		{
			lock (_connections) return _connections.Count;
		}
	}

	private void AcceptLoop()
	{
		while (!_disposed)
		{
			Socket socket;
			try
			{
				socket = _listener.AcceptSocket();
			}
			catch (SocketException)
			{
				if (_disposed) return;
				continue;
			}
			catch (ObjectDisposedException)
			{
				return;
			}

			socket.NoDelay = true;
			var connection = new HttpConnection(socket, _settings.MaxHeadBytes);
			bool accepted;
			lock (_connections)
			{
				accepted = _connections.Count < _settings.MaxConnections;
				if (accepted) _connections.Add(connection);
			}

			if (!accepted)
			{
				try
				{
					connection.WriteResponse(503, System.Text.Encoding.ASCII.GetBytes(_settings.BusyContentType), _settings.BusyBody, keepAlive: false);
				}
				catch (Exception ex) when (ex is IOException or SocketException)
				{
				}
				finally
				{
					connection.Dispose();
				}

				continue;
			}

			var thread = new Thread(() => Serve(connection)) { IsBackground = true, Name = _settings.Name + " connection" };
			thread.Start();
		}
	}

	private void Serve(HttpConnection connection)
	{
		try
		{
			connection.Socket.ReceiveTimeout = _settings.ReceiveTimeoutMs;
			_serve(connection);
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
		{
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "{Name}: connection failed.", _settings.Name);
		}
		finally
		{
			lock (_connections) _connections.Remove(connection);
			connection.Dispose();
		}
	}

	/// <summary>Stops accepting and closes every connection.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_listener.Stop();
		HttpConnection[] open;
		lock (_connections) open = [.. _connections];
		foreach (var connection in open) connection.Dispose();
	}
}
