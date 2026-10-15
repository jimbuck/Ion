using System.Globalization;
using System.Text;

using Ion.Extensions.Http;

namespace Ion.Extensions.Web;

/// <summary>
/// Serves files from one folder on the connection thread (no game-thread work): GET and HEAD, the default file for folders
/// (with a redirect to add the trailing slash), content types by extension, <c>ETag</c> and <c>If-None-Match</c>. Paths
/// with <c>..</c>, backslashes, NUL or a segment starting with <c>.</c> (hidden files) are not served, and the resolved
/// path must stay inside the folder.
/// </summary>
internal sealed class StaticFiles
{
	private readonly string _root;
	private readonly string _rootWithSeparator;
	private readonly string _defaultFile;
	private readonly long _maxBytes;

	public StaticFiles(string root, string defaultFile, long maxBytes)
	{
		_root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		_rootWithSeparator = _root + Path.DirectorySeparatorChar;
		_defaultFile = defaultFile;
		_maxBytes = maxBytes;
	}

	public string Root => _root;

	/// <summary>Resolves a request path to a file, or null. <paramref name="redirect"/> is set when the path names a folder without a trailing slash.</summary>
	public string? Resolve(ReadOnlySpan<byte> rawPath, out bool redirect)
	{
		redirect = false;
		var path = HttpText.DecodeToString(rawPath, plusIsSpace: false);
		if (path.Length == 0 || path[0] != '/' || path.Contains('\0', StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal)) return null;
		foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if (segment[0] == '.') return null; // "..", "." and hidden files
		}

		var relative = path.TrimStart('/');
		var full = Path.GetFullPath(Path.Join(_root, relative));
		if (!(full == _root || full.StartsWith(_rootWithSeparator, StringComparison.Ordinal))) return null;
		if (Directory.Exists(full))
		{
			if (!path.EndsWith('/'))
			{
				redirect = true;
				return full;
			}

			full = Path.Join(full, _defaultFile);
		}

		return File.Exists(full) ? full : null;
	}

	/// <summary>Answers a GET or HEAD request for <paramref name="rawPath"/>; false when there is no such file (the caller answers 404).</summary>
	public bool TryServe(HttpConnection connection, ReadOnlySpan<byte> rawPath, bool headOnly, bool keepAlive, ReadOnlySpan<byte> extraHeaders)
	{
		var file = Resolve(rawPath, out var redirect);
		if (file is null) return false;
		if (redirect)
		{
			// The raw path is already percent-encoded ASCII.
			var location = Encoding.ASCII.GetBytes("Location: " + Encoding.ASCII.GetString(rawPath) + "/\r\n");
			connection.WriteResponse(301, default, default, keepAlive, Concat(location, extraHeaders), headOnly);
			return true;
		}

		var info = new FileInfo(file);
		if (info.Length > _maxBytes)
		{
			connection.WriteResponse(413, "text/plain"u8, "The file is too large to serve."u8, keepAlive, extraHeaders, headOnly);
			return true;
		}

		var etag = "\"" + info.Length.ToString("x", CultureInfo.InvariantCulture) + "-" + info.LastWriteTimeUtc.Ticks.ToString("x", CultureInfo.InvariantCulture) + "\"";
		var headers = Concat(Encoding.ASCII.GetBytes("ETag: " + etag + "\r\nCache-Control: no-cache\r\n"), extraHeaders);
		if (connection.Request.TryGetHeader("If-None-Match"u8, out var match) && HttpText.ContainsToken(match, Encoding.ASCII.GetBytes(etag)))
		{
			connection.WriteResponse(304, default, default, keepAlive, headers, headOnly: true);
			return true;
		}

		// A HEAD answer announces the file's length, so the file is read either way.
		var body = File.ReadAllBytes(file);
		connection.WriteResponse(200, Encoding.ASCII.GetBytes(ContentType(file)), body, keepAlive, headers, headOnly);
		return true;
	}

	private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
	{
		var result = new byte[a.Length + b.Length];
		a.CopyTo(result);
		b.CopyTo(result.AsSpan(a.Length));
		return result;
	}

	/// <summary>The content type of a file by its extension.</summary>
	public static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
	{
		".html" or ".htm" => "text/html; charset=utf-8",
		".js" or ".mjs" => "text/javascript; charset=utf-8",
		".css" => "text/css; charset=utf-8",
		".json" or ".map" => "application/json",
		".txt" => "text/plain; charset=utf-8",
		".svg" => "image/svg+xml",
		".png" => "image/png",
		".jpg" or ".jpeg" => "image/jpeg",
		".gif" => "image/gif",
		".webp" => "image/webp",
		".ico" => "image/x-icon",
		".wasm" => "application/wasm",
		".woff" => "font/woff",
		".woff2" => "font/woff2",
		".ttf" => "font/ttf",
		".wav" => "audio/wav",
		".mp3" => "audio/mpeg",
		".ogg" => "audio/ogg",
		".webmanifest" => "application/manifest+json",
		_ => "application/octet-stream",
	};
}
