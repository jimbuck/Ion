using System.Buffers.Binary;
using System.Net.Sockets;

namespace Ion.Extensions.Http;

/// <summary>WebSocket frame opcodes (RFC 6455 section 5.2).</summary>
public enum WebSocketOpcode : byte
{
	/// <summary>A continuation frame.</summary>
	Continuation = 0x0,
	/// <summary>A UTF-8 text message.</summary>
	Text = 0x1,
	/// <summary>A binary message.</summary>
	Binary = 0x2,
	/// <summary>Close.</summary>
	Close = 0x8,
	/// <summary>Ping.</summary>
	Ping = 0x9,
	/// <summary>Pong.</summary>
	Pong = 0xA,
}

/// <summary>Limits of a server-side WebSocket.</summary>
public sealed class WebSocketSettings
{
	/// <summary>The largest message accepted from the client (after reassembling fragments); larger closes with 1009.</summary>
	public int MaxMessageBytes { get; init; } = 64 * 1024;

	/// <summary>How many outgoing messages may wait for the writer thread; a client that falls further behind is disconnected.</summary>
	public int SendQueueCapacity { get; init; } = 256;

	/// <summary>The writer thread's name.</summary>
	public string WriterThreadName { get; init; } = "Ion WebSocket writer";
}

/// <summary>
/// The server side of one WebSocket (RFC 6455) after the opening handshake: <see cref="ReadMessage"/> on the connection's
/// thread (control frames are answered there), and <see cref="Send"/> from any thread, which copies the message into a
/// bounded ring drained by a writer thread, so a slow client never blocks the sender. Buffers are reused: in steady state
/// neither direction allocates. No extensions (the RSV bits must be zero); client frames must be masked.
/// </summary>
public sealed class WebSocketConnection : IDisposable
{
	private readonly Socket _socket;
	private readonly WebSocketSettings _settings;
	private readonly byte[] _read = new byte[8192];
	private int _readPosition, _readLength;
	private byte[] _message = new byte[1024];
	private readonly byte[]?[] _slots;
	// Frame buffers the writer has sent, reused by the next sends: a client that keeps up uses a handful of buffers.
	private readonly Stack<byte[]> _spare = new();
	private readonly int[] _slotLengths;
	private readonly Lock _sendLock = new();
	private readonly SemaphoreSlim _pending = new(0, int.MaxValue);
	private readonly Thread _writer;
	private int _head, _count;
	private volatile bool _closed;
	private bool _closeSent;
	private bool _disposed;

	internal WebSocketConnection(Socket socket, ReadOnlySpan<byte> leftover, WebSocketSettings settings)
	{
		_socket = socket;
		_settings = settings;
		leftover.CopyTo(_read);
		_readLength = leftover.Length;
		var capacity = Math.Max(4, settings.SendQueueCapacity);
		_slots = new byte[capacity][];
		_slotLengths = new int[capacity];
		// A few frame buffers up front: a send can overlap the writer finishing the previous one.
		for (var i = 0; i < 4; i++) _spare.Push(new byte[256]);
		_socket.ReceiveTimeout = 0;
		_writer = new Thread(WriteLoop) { IsBackground = true, Name = settings.WriterThreadName };
		_writer.Start();
	}

	/// <summary>Whether the socket is closed (by either side, or because the client fell behind).</summary>
	public bool IsClosed => _closed;

	/// <summary>The number of messages sent.</summary>
	public long SentCount { get; private set; }

	/// <summary>
	/// Reads the next complete text or binary message (fragments reassembled). Pings are answered with pongs, pongs are
	/// ignored, a close frame is answered and ends the session. Returns false when the session ended (closed by the peer,
	/// a protocol error, or a message over the limit, each answered with the matching close code). The payload is valid
	/// until the next call.
	/// </summary>
	public bool ReadMessage(out WebSocketOpcode opcode, out ReadOnlySpan<byte> payload)
	{
		opcode = WebSocketOpcode.Text;
		payload = default;
		var length = 0;
		var started = false;
		try
		{
			Span<byte> header = stackalloc byte[2];
			Span<byte> mask = stackalloc byte[4];
			Span<byte> ext = stackalloc byte[8];
			Span<byte> control = stackalloc byte[125];
			while (!_closed)
			{
				if (!TryRead(header)) return Ended();
				var fin = (header[0] & 0x80) != 0;
				var frameOpcode = (WebSocketOpcode)(header[0] & 0x0F);
				if ((header[0] & 0x70) != 0) return Fail(1002);
				if ((header[1] & 0x80) == 0) return Fail(1002); // client frames must be masked
				long frameLength = header[1] & 0x7F;
				if (frameLength == 126)
				{
					if (!TryRead(ext[..2])) return Ended();
					frameLength = BinaryPrimitives.ReadUInt16BigEndian(ext);
				}
				else if (frameLength == 127)
				{
					if (!TryRead(ext)) return Ended();
					frameLength = (long)BinaryPrimitives.ReadUInt64BigEndian(ext);
					if (frameLength < 0) return Fail(1009);
				}

				if (!TryRead(mask)) return Ended();
				var isControl = ((byte)frameOpcode & 0x8) != 0;
				if (isControl)
				{
					if (!fin || frameLength > 125) return Fail(1002);
					var body = control[..(int)frameLength];
					if (!TryRead(body)) return Ended();
					Unmask(body, mask, 0);
					switch (frameOpcode)
					{
						case WebSocketOpcode.Ping:
							Send(body, WebSocketOpcode.Pong);
							break;
						case WebSocketOpcode.Pong:
							break;
						case WebSocketOpcode.Close:
							Close(body.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(body) : (ushort)1000);
							return false;
						default:
							return Fail(1002);
					}

					continue;
				}

				if (frameOpcode == WebSocketOpcode.Continuation)
				{
					if (!started) return Fail(1002);
				}
				else if (frameOpcode is WebSocketOpcode.Text or WebSocketOpcode.Binary)
				{
					if (started) return Fail(1002);
					started = true;
					opcode = frameOpcode;
				}
				else
				{
					return Fail(1002);
				}

				if (length + frameLength > _settings.MaxMessageBytes) return Fail(1009);
				var needed = length + (int)frameLength;
				if (_message.Length < needed) Array.Resize(ref _message, Math.Min(Math.Max(needed, _message.Length * 2), Math.Max(needed, _settings.MaxMessageBytes)));
				var target = _message.AsSpan(length, (int)frameLength);
				if (!TryRead(target)) return Ended();
				Unmask(target, mask, 0);
				length = needed;
				if (fin)
				{
					payload = _message.AsSpan(0, length);
					return true;
				}
			}

			return false;
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
		{
			return Ended();
		}
	}

	/// <summary>
	/// Queues a message for the writer thread (the payload is copied). Safe from any thread and never blocks on the
	/// network. Returns false when the socket is closed; a client whose queue is full is disconnected (and false returned).
	/// </summary>
	public bool Send(ReadOnlySpan<byte> payload, WebSocketOpcode opcode = WebSocketOpcode.Text)
	{
		lock (_sendLock)
		{
			if (_closed || _closeSent) return false;
			if (_count == _slots.Length)
			{
				// A client that stops reading loses its connection rather than stalling the game.
				Abort();
				return false;
			}

			EnqueueFrame(opcode, payload);
			if (opcode == WebSocketOpcode.Close) _closeSent = true;
		}

		_pending.Release();
		return true;
	}

	/// <summary>Sends a close frame with <paramref name="code"/> and ends the session once it is written.</summary>
	public void Close(ushort code = 1000)
	{
		Span<byte> body = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16BigEndian(body, code);
		if (!Send(body, WebSocketOpcode.Close)) _closed = true;
		_pending.Release();
	}

	private void EnqueueFrame(WebSocketOpcode opcode, ReadOnlySpan<byte> payload)
	{
		var index = (_head + _count) % _slots.Length;
		var size = payload.Length + 10;
		var slot = _spare.Count > 0 ? _spare.Pop() : null;
		if (slot is null || slot.Length < size) slot = new byte[Math.Max(size, 256)];
		_slots[index] = slot;
		slot[0] = (byte)(0x80 | (byte)opcode);
		int headerLength;
		if (payload.Length < 126)
		{
			slot[1] = (byte)payload.Length;
			headerLength = 2;
		}
		else if (payload.Length <= ushort.MaxValue)
		{
			slot[1] = 126;
			BinaryPrimitives.WriteUInt16BigEndian(slot.AsSpan(2), (ushort)payload.Length);
			headerLength = 4;
		}
		else
		{
			slot[1] = 127;
			BinaryPrimitives.WriteUInt64BigEndian(slot.AsSpan(2), (ulong)payload.Length);
			headerLength = 10;
		}

		payload.CopyTo(slot.AsSpan(headerLength));
		_slotLengths[index] = headerLength + payload.Length;
		_count++;
	}

	private void WriteLoop()
	{
		try
		{
			while (true)
			{
				_pending.Wait();
				byte[] frame;
				int length;
				lock (_sendLock)
				{
					// Closed without a close frame (the peer went away, or fell behind): stop at once.
					if (_closed && !_closeSent) return;
					if (_count == 0)
					{
						if (_closed) return;
						continue;
					}

					frame = _slots[_head]!;
					length = _slotLengths[_head];
				}

				var span = frame.AsSpan(0, length);
				while (span.Length > 0)
				{
					var sent = _socket.Send(span);
					if (sent <= 0) return;
					span = span[sent..];
				}

				bool wasClose;
				lock (_sendLock)
				{
					wasClose = (frame[0] & 0x0F) == (byte)WebSocketOpcode.Close;
					_slots[_head] = null;
					if (_spare.Count < 16) _spare.Push(frame);
					_head = (_head + 1) % _slots.Length;
					_count--;
					SentCount++;
				}

				if (wasClose) return;
			}
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
		{
		}
		finally
		{
			_closed = true;
			try
			{
				_socket.Shutdown(SocketShutdown.Both);
			}
			catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
			{
			}
		}
	}

	private bool TryRead(Span<byte> destination)
	{
		var filled = 0;
		while (filled < destination.Length)
		{
			if (_readPosition < _readLength)
			{
				var n = Math.Min(destination.Length - filled, _readLength - _readPosition);
				_read.AsSpan(_readPosition, n).CopyTo(destination[filled..]);
				_readPosition += n;
				filled += n;
				continue;
			}

			var remaining = destination.Length - filled;
			if (remaining >= _read.Length)
			{
				var direct = _socket.Receive(destination[filled..]);
				if (direct <= 0) return false;
				filled += direct;
				continue;
			}

			_readPosition = 0;
			_readLength = _socket.Receive(_read);
			if (_readLength <= 0)
			{
				_readLength = 0;
				return false;
			}
		}

		return true;
	}

	private static void Unmask(Span<byte> data, ReadOnlySpan<byte> mask, int offset)
	{
		for (var i = 0; i < data.Length; i++) data[i] ^= mask[(i + offset) & 3];
	}

	private bool Fail(ushort code)
	{
		Close(code);
		return false;
	}

	/// <summary>Ends the session at once, without a close frame: pending messages are dropped and the socket is shut down.</summary>
	public void Abort()
	{
		_closed = true;
		try
		{
			_socket.Shutdown(SocketShutdown.Both);
		}
		catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
		{
		}

		_pending.Release();
	}

	private bool Ended()
	{
		_closed = true;
		_pending.Release();
		return false;
	}

	/// <summary>Closes the session: pending messages are dropped and the socket is shut down.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_closed = true;
		_pending.Release();
		_writer.Join(TimeSpan.FromSeconds(1));
		try
		{
			_socket.Shutdown(SocketShutdown.Both);
		}
		catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
		{
		}

		_socket.Dispose();
	}
}
