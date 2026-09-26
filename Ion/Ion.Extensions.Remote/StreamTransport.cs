using System.Text;

namespace Ion.Extensions.Remote;

/// <summary>
/// The stdio transport over any pair of streams: one JSON-RPC message per line (UTF-8, <c>\n</c>-terminated) in each
/// direction. A reader thread queues requests; a writer thread writes responses. The end of the input closes the session.
/// </summary>
internal sealed class StreamTransport : StreamingConnection
{
	private readonly RemoteServer _server;
	private readonly Stream _input;
	private readonly Stream _output;
	private readonly Thread _reader;

	public StreamTransport(RemoteServer server, Stream input, Stream output, RemoteAccess access, string name)
		: base("stdio", access, $"Ion remote {name} writer")
	{
		_server = server;
		_input = input;
		_output = output;
		_reader = new Thread(ReadLoop) { IsBackground = true, Name = $"Ion remote {name} reader" };
		StartWriter();
		_reader.Start();
	}

	protected override void WriteMessage(byte[] message)
	{
		_output.Write(message);
		_output.WriteByte((byte)'\n');
		_output.Flush();
	}

	protected override void CloseStream()
	{
		_output.Dispose();
	}

	private void ReadLoop()
	{
		var max = _server.Options.MaxRequestBytes;
		var line = new MemoryStream();
		var buffer = new byte[8192];
		try
		{
			while (!IsClosed)
			{
				var read = _input.Read(buffer, 0, buffer.Length);
				if (read <= 0) break;

				var start = 0;
				for (var i = 0; i < read; i++)
				{
					if (buffer[i] != (byte)'\n') continue;
					line.Write(buffer, start, i - start);
					Dispatch(line);
					start = i + 1;
				}

				line.Write(buffer, start, read - start);
				if (line.Length > max)
				{
					Send(RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, $"Message larger than {max} bytes."));
					break;
				}
			}
		}
		catch (IOException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
		finally
		{
			Close();
		}
	}

	private void Dispatch(MemoryStream line)
	{
		var span = line.GetBuffer().AsSpan(0, (int)line.Length);
		if (span.Length > 0 && span[^1] == (byte)'\r') span = span[..^1];
		if (!span.IsWhiteSpace()) _server.HandleIncoming(this, span);
		line.SetLength(0);
	}
}

internal static class SpanExtensions
{
	public static bool IsWhiteSpace(this Span<byte> span)
	{
		foreach (var b in span)
		{
			if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) return false;
		}

		return true;
	}

	public static string Ascii(this ReadOnlySpan<byte> span) => Encoding.ASCII.GetString(span);
}
