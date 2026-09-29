using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Ion.Extensions.Web;

/// <summary>
/// The response an <see cref="HttpAttribute"/> handler writes, on the game thread: status, content type, extra headers
/// and body. A handle over a buffer the server reuses for every request of a connection, so writing a small response
/// allocates nothing. A handler that writes nothing answers <c>204 No Content</c>; the generated code writes the method's
/// result here.
/// </summary>
public readonly struct WebResponse
{
	private readonly WebResponseWriter _writer;

	/// <summary>Wraps <paramref name="writer"/>.</summary>
	public WebResponse(WebResponseWriter writer)
	{
		ArgumentNullException.ThrowIfNull(writer);
		_writer = writer;
	}

	private WebResponseWriter Writer => _writer ?? throw new InvalidOperationException("This WebResponse was not created by the web server.");

	/// <summary>The status code (200 unless set; 204 is sent when nothing was written).</summary>
	public int StatusCode => Writer.Status;

	/// <summary>Whether a status or a body was written.</summary>
	public bool IsWritten => Writer.IsWritten;

	/// <summary>The body written so far.</summary>
	public ReadOnlySpan<byte> Body => Writer.WrittenSpan;

	/// <summary>The content type (null when none was set).</summary>
	public string? ContentType => Writer.ContentType;

	/// <summary>The body as a buffer writer (set <see cref="SetContentType"/> too).</summary>
	public IBufferWriter<byte> BodyWriter => Writer;

	/// <summary>Sets the status code.</summary>
	public void SetStatus(int status) => Writer.SetStatus(status);

	/// <summary>Sets the content type (a constant string does not allocate).</summary>
	public void SetContentType(string contentType) => Writer.SetContentType(contentType);

	/// <summary>Adds a response header (name and value must be ASCII without CR or LF).</summary>
	public void Header(string name, string value) => Writer.AddHeader(name, value);

	/// <summary>Writes <paramref name="text"/> as <c>text/plain; charset=utf-8</c>.</summary>
	public void Text(ReadOnlySpan<char> text, int status = 200) => Writer.WriteText(text, "text/plain; charset=utf-8", status);

	/// <summary>Writes <paramref name="html"/> as <c>text/html; charset=utf-8</c>.</summary>
	public void Html(ReadOnlySpan<char> html, int status = 200) => Writer.WriteText(html, "text/html; charset=utf-8", status);

	/// <summary>Writes raw bytes with <paramref name="contentType"/>.</summary>
	public void Bytes(ReadOnlySpan<byte> body, string contentType, int status = 200) => Writer.WriteBytes(body, contentType, status);

	/// <summary>Writes <paramref name="value"/> as JSON with its source-generated type information (NativeAOT-safe).</summary>
	public void Json<T>(T value, JsonTypeInfo<T> type, int status = 200) => Writer.WriteJson(value, type, status);

	/// <summary>Writes a JSON number.</summary>
	public void JsonNumber(long value, int status = 200) => Writer.WriteJsonNumber(value, status);

	/// <summary>Writes a JSON number.</summary>
	public void JsonNumber(ulong value, int status = 200) => Writer.WriteJsonNumber(value, status);

	/// <summary>Writes a JSON number (non-finite values are written as <c>null</c>).</summary>
	public void JsonNumber(double value, int status = 200) => Writer.WriteJsonNumber(value, status);

	/// <summary>Writes a JSON number.</summary>
	public void JsonNumber(decimal value, int status = 200) => Writer.WriteJsonNumber(value, status);

	/// <summary>Writes a JSON boolean.</summary>
	public void JsonBool(bool value, int status = 200) => Writer.WriteRawJson(value ? "true"u8 : "false"u8, status);

	/// <summary>Writes JSON <c>null</c>.</summary>
	public void JsonNull(int status = 200) => Writer.WriteRawJson("null"u8, status);

	/// <summary>Writes an error: <paramref name="status"/> and <paramref name="message"/> as text.</summary>
	public void Error(int status, string message) => Writer.WriteText(message, "text/plain; charset=utf-8", status);
}

/// <summary>
/// The reusable buffer behind a <see cref="WebResponse"/>: one per connection, reset for every request. Also an
/// <see cref="IBufferWriter{T}"/> for the body.
/// </summary>
public sealed class WebResponseWriter : IBufferWriter<byte>
{
	private byte[] _body = new byte[1024];
	private int _count;
	private byte[] _headers = new byte[256];
	private int _headerCount;
	private Utf8JsonWriter? _json;

	/// <summary>The status code.</summary>
	public int Status { get; private set; } = 200;

	/// <summary>Whether a status, content type or body was written.</summary>
	public bool IsWritten { get; private set; }

	/// <summary>The content type, or null.</summary>
	public string? ContentType { get; private set; }

	/// <summary>The body written so far.</summary>
	public ReadOnlySpan<byte> WrittenSpan => _body.AsSpan(0, _count);

	/// <summary>The extra header lines written so far (each ending in CRLF).</summary>
	public ReadOnlySpan<byte> HeaderBytes => _headers.AsSpan(0, _headerCount);

	/// <summary>Clears everything for the next request.</summary>
	public void Reset()
	{
		Status = 200;
		IsWritten = false;
		ContentType = null;
		_count = 0;
		_headerCount = 0;
	}

	/// <summary>Sets the status code.</summary>
	public void SetStatus(int status)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(status, 100);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 599);
		Status = status;
		IsWritten = true;
	}

	/// <summary>Sets the content type.</summary>
	public void SetContentType(string contentType)
	{
		ArgumentException.ThrowIfNullOrEmpty(contentType);
		CheckHeaderText(contentType);
		ContentType = contentType;
		IsWritten = true;
	}

	/// <summary>Adds a header line.</summary>
	public void AddHeader(string name, string value)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(value);
		CheckHeaderText(name);
		CheckHeaderText(value);
		var needed = _headerCount + name.Length + value.Length + 4;
		if (_headers.Length < needed) Array.Resize(ref _headers, Math.Max(needed, _headers.Length * 2));
		_headerCount += Encoding.ASCII.GetBytes(name, _headers.AsSpan(_headerCount));
		_headers[_headerCount++] = (byte)':';
		_headers[_headerCount++] = (byte)' ';
		_headerCount += Encoding.ASCII.GetBytes(value, _headers.AsSpan(_headerCount));
		_headers[_headerCount++] = (byte)'\r';
		_headers[_headerCount++] = (byte)'\n';
	}

	/// <summary>Replaces the body with <paramref name="text"/> in UTF-8.</summary>
	public void WriteText(ReadOnlySpan<char> text, string contentType, int status)
	{
		Begin(contentType, status);
		var span = GetSpan(Encoding.UTF8.GetMaxByteCount(text.Length));
		Advance(Encoding.UTF8.GetBytes(text, span));
	}

	/// <summary>Replaces the body with <paramref name="body"/>.</summary>
	public void WriteBytes(ReadOnlySpan<byte> body, string contentType, int status)
	{
		Begin(contentType, status);
		body.CopyTo(GetSpan(body.Length));
		Advance(body.Length);
	}

	/// <summary>Replaces the body with raw JSON.</summary>
	public void WriteRawJson(ReadOnlySpan<byte> json, int status) => WriteBytes(json, "application/json", status);

	/// <summary>Replaces the body with a JSON number.</summary>
	public void WriteJsonNumber(long value, int status)
	{
		Begin("application/json", status);
		Utf8Formatter.TryFormat(value, GetSpan(24), out var written);
		Advance(written);
	}

	/// <summary>Replaces the body with a JSON number.</summary>
	public void WriteJsonNumber(ulong value, int status)
	{
		Begin("application/json", status);
		Utf8Formatter.TryFormat(value, GetSpan(24), out var written);
		Advance(written);
	}

	/// <summary>Replaces the body with a JSON number (<c>null</c> when not finite).</summary>
	public void WriteJsonNumber(double value, int status)
	{
		if (!double.IsFinite(value))
		{
			WriteRawJson("null"u8, status);
			return;
		}

		Begin("application/json", status);
		value.TryFormat(GetSpan(32), out var written, "R", System.Globalization.CultureInfo.InvariantCulture);
		Advance(written);
	}

	/// <summary>Replaces the body with a JSON number.</summary>
	public void WriteJsonNumber(decimal value, int status)
	{
		Begin("application/json", status);
		value.TryFormat(GetSpan(40), out var written, default, System.Globalization.CultureInfo.InvariantCulture);
		Advance(written);
	}

	/// <summary>Replaces the body with <paramref name="value"/> serialized as JSON.</summary>
	public void WriteJson<T>(T value, JsonTypeInfo<T> type, int status)
	{
		ArgumentNullException.ThrowIfNull(type);
		Begin("application/json", status);
		if (_json is null) _json = new Utf8JsonWriter(this, new JsonWriterOptions { SkipValidation = true });
		else _json.Reset(this);
		JsonSerializer.Serialize(_json, value, type);
		_json.Flush();
	}

	private void Begin(string contentType, int status)
	{
		SetStatus(status);
		ContentType = contentType;
		_count = 0;
	}

	/// <inheritdoc/>
	public void Advance(int count)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		if (_count + count > _body.Length) throw new InvalidOperationException("Advanced past the buffer.");
		_count += count;
		IsWritten = true;
	}

	/// <inheritdoc/>
	public Memory<byte> GetMemory(int sizeHint = 0)
	{
		Ensure(sizeHint);
		return _body.AsMemory(_count);
	}

	/// <inheritdoc/>
	public Span<byte> GetSpan(int sizeHint = 0)
	{
		Ensure(sizeHint);
		return _body.AsSpan(_count);
	}

	private void Ensure(int sizeHint)
	{
		var needed = _count + Math.Max(sizeHint, 1);
		if (needed > _body.Length) Array.Resize(ref _body, Math.Max(needed, _body.Length * 2));
	}

	private static void CheckHeaderText(string text)
	{
		foreach (var c in text)
		{
			if (c is '\r' or '\n' || c > 0x7E || (c < 0x20 && c != '\t')) throw new ArgumentException("Header text must be printable ASCII without CR or LF.", nameof(text));
		}
	}
}
