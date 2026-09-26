using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Ion.Extensions.Remote;

/// <summary>
/// One client of the remote server: an HTTP exchange, a WebSocket or the stdio stream. <see cref="Send"/> is thread safe
/// and never blocks the game thread (streaming connections queue messages for a writer thread).
/// </summary>
internal abstract class RemoteConnection
{
	private static int _nextId;

	protected RemoteConnection(string transport, RemoteAccess access)
	{
		Transport = transport;
		Access = access;
		Id = Interlocked.Increment(ref _nextId);
	}

	/// <summary>A process-unique number, for logs.</summary>
	public int Id { get; }

	/// <summary><c>http</c>, <c>ws</c> or <c>stdio</c>.</summary>
	public string Transport { get; }

	/// <summary>The session's access: <see cref="RemoteAccess.Mutate"/> includes read.</summary>
	public RemoteAccess Access { get; }

	/// <summary>Whether the connection can carry several responses per request (watches).</summary>
	public abstract bool CanStream { get; }

	/// <summary>Whether the connection is closed (its watches are dropped).</summary>
	public abstract bool IsClosed { get; }

	/// <summary>Sends one JSON-RPC message.</summary>
	public abstract void Send(byte[] message);
}

/// <summary>
/// A connection that writes messages from a dedicated thread, so a slow client never blocks the sender. Used by the
/// WebSocket and stdio transports.
/// </summary>
internal abstract class StreamingConnection : RemoteConnection, IDisposable
{
	private readonly BlockingCollection<byte[]> _outgoing = new(boundedCapacity: 4096);
	private readonly Thread _writer;
	private volatile bool _closed;

	protected StreamingConnection(string transport, RemoteAccess access, string threadName) : base(transport, access)
	{
		_writer = new Thread(WriteLoop) { IsBackground = true, Name = threadName };
	}

	public override bool CanStream => true;

	public override bool IsClosed => _closed;

	protected void StartWriter() => _writer.Start();

	public override void Send(byte[] message)
	{
		if (_closed) return;
		// A client that stops reading loses messages rather than stalling the game.
		if (!_outgoing.TryAdd(message)) Close();
	}

	/// <summary>Writes one message to the underlying stream (on the writer thread).</summary>
	protected abstract void WriteMessage(byte[] message);

	/// <summary>Closes the underlying stream.</summary>
	protected abstract void CloseStream();

	public void Close()
	{
		if (_closed) return;
		_closed = true;
		_outgoing.CompleteAdding();
	}

	private void WriteLoop()
	{
		try
		{
			foreach (var message in _outgoing.GetConsumingEnumerable())
			{
				WriteMessage(message);
			}
		}
		catch (Exception)
		{
			// The peer went away; the reader notices and closes too.
		}
		finally
		{
			_closed = true;
			try
			{
				CloseStream();
			}
			catch (Exception)
			{
			}
		}
	}

	public void Dispose() => Close();
}

/// <summary>A request queued for the game thread.</summary>
internal sealed record PendingRequest(RemoteConnection Connection, JsonNode? Id, string Method, JsonNode? Params, bool Watch)
{
	/// <summary>The request id as JSON text, or null for a notification.</summary>
	public string? IdText { get; } = Id?.ToJsonString();
}
