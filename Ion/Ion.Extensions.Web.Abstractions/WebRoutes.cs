using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Ion.Extensions.Http;

namespace Ion.Extensions.Web;

/// <summary>Calls one <see cref="HttpAttribute"/> method on its system (generated code: binds the parameters, calls, writes the result).</summary>
/// <param name="target">The system instance.</param>
/// <param name="request">The request.</param>
/// <param name="response">The response to write.</param>
public delegate void WebHandler(object target, in WebRequest request, WebResponse response);

/// <summary>Calls one <see cref="WebSocketAttribute"/> method on its system (generated code).</summary>
/// <param name="target">The system instance.</param>
/// <param name="message">The connection change or message.</param>
public delegate void WebSocketHandler(object target, in WebSocketMessage message);

/// <summary>
/// One HTTP endpoint of the route table: method, path template, the access it needs, and how to find and call its handler.
/// The routing generator creates one per <see cref="HttpAttribute"/>; games and tests can create them by hand.
/// </summary>
public sealed class WebRoute
{
	private readonly Segment[] _segments;

	/// <summary>Creates a route.</summary>
	/// <param name="method">The HTTP method (<c>GET</c>, <c>HEAD</c>, <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c>).</param>
	/// <param name="template">The path template (see <see cref="HttpAttribute"/>).</param>
	/// <param name="systemType">The type declaring the handler (the service resolved as its target).</param>
	/// <param name="resolve">Finds the handler's target in the application's services.</param>
	/// <param name="handler">Calls the handler.</param>
	/// <param name="access">The access it needs (<see cref="WebAccess.Auto"/>: read for GET and HEAD, mutate otherwise).</param>
	/// <param name="name">A display name (<c>Type.Method</c>).</param>
	/// <exception cref="ArgumentException">The method or template is not valid.</exception>
	public WebRoute(string method, string template, Type systemType, Func<IServiceProvider, object?> resolve, WebHandler handler, WebAccess access = WebAccess.Auto, string? name = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(method);
		ArgumentNullException.ThrowIfNull(systemType);
		ArgumentNullException.ThrowIfNull(resolve);
		ArgumentNullException.ThrowIfNull(handler);
		Verb = ParseMethod(method) ?? throw new ArgumentException($"Unsupported HTTP method '{method}' (GET, HEAD, POST, PUT, PATCH or DELETE).", nameof(method));
		if (!TryParseTemplate(template, out var segments, out var error)) throw new ArgumentException($"Invalid route template '{template}': {error}", nameof(template));
		_segments = segments;
		Method = method.ToUpperInvariant();
		Template = template;
		SystemType = systemType;
		Resolve = resolve;
		Handler = handler;
		Access = access != WebAccess.Auto ? access : Verb is HttpVerb.Get or HttpVerb.Head ? WebAccess.Read : WebAccess.Mutate;
		Name = name ?? $"{systemType.Name}.{Method} {template}";
		ParameterNames = [.. segments.Where(static s => s.Kind != SegmentKind.Literal).Select(static s => s.Name!)];
	}

	/// <summary>The method, upper case.</summary>
	public string Method { get; }

	/// <summary>The method as the parser reports it.</summary>
	public HttpVerb Verb { get; }

	/// <summary>The path template.</summary>
	public string Template { get; }

	/// <summary>The access it needs: <see cref="WebAccess.Read"/> or <see cref="WebAccess.Mutate"/> (never <see cref="WebAccess.Auto"/>).</summary>
	public WebAccess Access { get; }

	/// <summary>The type declaring the handler.</summary>
	public Type SystemType { get; }

	/// <summary>Finds the handler's target.</summary>
	public Func<IServiceProvider, object?> Resolve { get; }

	/// <summary>Calls the handler.</summary>
	public WebHandler Handler { get; }

	/// <summary>A display name.</summary>
	public string Name { get; }

	/// <summary>The names of the <c>{name}</c> and <c>{*name}</c> segments, in order.</summary>
	public IReadOnlyList<string> ParameterNames { get; }

	/// <summary>The number of route values a match produces.</summary>
	public int RouteValueCount => ParameterNames.Count;

	/// <summary>
	/// A key that is equal for two routes that match the same paths (<c>/a/{x}</c> and <c>/a/{y}</c>): the method and the
	/// template with parameter names erased and literals in lower case.
	/// </summary>
	public string ShapeKey => Method + " " + ShapeOf(_segments);

	/// <summary>
	/// Matches <paramref name="rawPath"/> (percent-encoded, starting with <c>/</c>) against the template, writing the route
	/// values' ranges into <paramref name="values"/> (at least <see cref="RouteValueCount"/> long). Literal segments compare
	/// ASCII case-insensitively; one trailing slash is ignored. Allocation-free.
	/// </summary>
	public bool TryMatch(ReadOnlySpan<byte> rawPath, Span<Range> values)
	{
		if (rawPath.IsEmpty || rawPath[0] != (byte)'/') return false;
		if (rawPath.Length > 1 && rawPath[^1] == (byte)'/') rawPath = rawPath[..^1];
		var position = 1;
		var valueIndex = 0;
		for (var i = 0; i < _segments.Length; i++)
		{
			var segment = _segments[i];
			if (segment.Kind == SegmentKind.CatchAll)
			{
				values[valueIndex++] = new Range(Math.Min(position, rawPath.Length), rawPath.Length);
				return true;
			}

			if (position > rawPath.Length) return false;
			var rest = rawPath[position..];
			var slash = rest.IndexOf((byte)'/');
			var length = slash < 0 ? rest.Length : slash;
			var part = rest[..length];
			if (segment.Kind == SegmentKind.Literal)
			{
				if (!Ascii.EqualsIgnoreCase(part, segment.Literal)) return false;
			}
			else
			{
				if (part.IsEmpty) return false;
				values[valueIndex++] = new Range(position, position + length);
			}

			position += length + 1;
		}

		// Every segment matched: the path must end here (the root template "/" has no segments).
		return position >= rawPath.Length || (_segments.Length == 0 && rawPath.Length == 1);
	}

	/// <summary>Orders routes from the most specific: literal segments before parameters, parameters before a catch-all.</summary>
	public static int CompareSpecificity(WebRoute a, WebRoute b)
	{
		ArgumentNullException.ThrowIfNull(a);
		ArgumentNullException.ThrowIfNull(b);
		var count = Math.Min(a._segments.Length, b._segments.Length);
		for (var i = 0; i < count; i++)
		{
			var c = ((int)a._segments[i].Kind).CompareTo((int)b._segments[i].Kind);
			if (c != 0) return c;
		}

		return b._segments.Length.CompareTo(a._segments.Length);
	}

	/// <summary>The method named <paramref name="method"/>, or null when the route table does not support it.</summary>
	public static HttpVerb? ParseMethod(string method) => method.ToUpperInvariant() switch
	{
		"GET" => HttpVerb.Get,
		"HEAD" => HttpVerb.Head,
		"POST" => HttpVerb.Post,
		"PUT" => HttpVerb.Put,
		"PATCH" => HttpVerb.Patch,
		"DELETE" => HttpVerb.Delete,
		_ => null,
	};

	/// <summary>Checks a template: <c>/</c>, then segments separated by <c>/</c>, each a literal, <c>{name}</c>, or a last <c>{*name}</c>.</summary>
	public static bool IsValidTemplate(string template, out string? error) => TryParseTemplate(template, out _, out error);

	private static bool TryParseTemplate(string template, out Segment[] segments, out string? error)
	{
		segments = [];
		error = null;
		if (string.IsNullOrEmpty(template) || template[0] != '/')
		{
			error = "it must start with '/'";
			return false;
		}

		if (template.Contains('?', StringComparison.Ordinal) || template.Contains('#', StringComparison.Ordinal))
		{
			error = "it cannot contain a query or a fragment";
			return false;
		}

		var body = template.Length > 1 && template[^1] == '/' ? template[1..^1] : template[1..];
		if (body.Length == 0) return true;
		var parts = body.Split('/');
		var list = new List<Segment>(parts.Length);
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < parts.Length; i++)
		{
			var part = parts[i];
			if (part.Length == 0)
			{
				error = "it has an empty segment ('//')";
				return false;
			}

			if (part[0] == '{')
			{
				if (part[^1] != '}' || part.Length < 3)
				{
					error = $"segment '{part}' must be '{{name}}' or '{{*name}}'";
					return false;
				}

				var catchAll = part[1] == '*';
				var name = part[(catchAll ? 2 : 1)..^1];
				if (name.Length == 0 || !IsIdentifier(name))
				{
					error = $"'{name}' in '{part}' is not a parameter name";
					return false;
				}

				if (!names.Add(name))
				{
					error = $"parameter '{name}' appears twice";
					return false;
				}

				if (catchAll && i != parts.Length - 1)
				{
					error = $"the catch-all '{part}' must be the last segment";
					return false;
				}

				list.Add(new Segment(catchAll ? SegmentKind.CatchAll : SegmentKind.Parameter, name, []));
			}
			else
			{
				if (part.Contains('{', StringComparison.Ordinal) || part.Contains('}', StringComparison.Ordinal))
				{
					error = $"segment '{part}' mixes a literal and a parameter";
					return false;
				}

				foreach (var c in part)
				{
					if (c <= 0x20 || c >= 0x7F)
					{
						error = $"segment '{part}' has a character that must be percent-encoded";
						return false;
					}
				}

				list.Add(new Segment(SegmentKind.Literal, null, Encoding.ASCII.GetBytes(part)));
			}
		}

		segments = [.. list];
		return true;
	}

	private static bool IsIdentifier(string name)
	{
		if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;
		foreach (var c in name)
		{
			if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
		}

		return true;
	}

	private static string ShapeOf(Segment[] segments)
	{
		if (segments.Length == 0) return "/";
		var builder = new StringBuilder();
		foreach (var s in segments)
		{
			builder.Append('/');
			builder.Append(s.Kind switch
			{
				SegmentKind.Literal => Encoding.ASCII.GetString(s.Literal).ToLowerInvariant(),
				SegmentKind.Parameter => "{}",
				_ => "{*}",
			});
		}

		return builder.ToString();
	}

	private enum SegmentKind
	{
		Literal,
		Parameter,
		CatchAll,
	}

	private readonly record struct Segment(SegmentKind Kind, string? Name, byte[] Literal);
}

/// <summary>
/// One WebSocket endpoint of the route table: its path (literal), the access it needs, and its handler. The routing
/// generator creates one per <see cref="WebSocketAttribute"/>.
/// </summary>
public sealed class WebSocketRoute
{
	/// <summary>Creates a WebSocket route.</summary>
	/// <exception cref="ArgumentException">The path is not a literal path starting with <c>/</c>.</exception>
	public WebSocketRoute(string path, Type systemType, Func<IServiceProvider, object?> resolve, WebSocketHandler handler, WebAccess access = WebAccess.Auto, string? name = null)
	{
		ArgumentNullException.ThrowIfNull(systemType);
		ArgumentNullException.ThrowIfNull(resolve);
		ArgumentNullException.ThrowIfNull(handler);
		if (!IsValidPath(path, out var error)) throw new ArgumentException($"Invalid WebSocket path '{path}': {error}", nameof(path));
		Path = path.Length > 1 && path[^1] == '/' ? path[..^1] : path;
		PathBytes = Encoding.ASCII.GetBytes(Path);
		SystemType = systemType;
		Resolve = resolve;
		Handler = handler;
		Access = access == WebAccess.Auto ? WebAccess.Mutate : access;
		Name = name ?? $"{systemType.Name} WS {Path}";
	}

	/// <summary>The path.</summary>
	public string Path { get; }

	/// <summary>The path as ASCII bytes.</summary>
	public byte[] PathBytes { get; }

	/// <summary>The access connecting needs: <see cref="WebAccess.Mutate"/> unless declared <see cref="WebAccess.Read"/>.</summary>
	public WebAccess Access { get; }

	/// <summary>The type declaring the handler.</summary>
	public Type SystemType { get; }

	/// <summary>Finds the handler's target.</summary>
	public Func<IServiceProvider, object?> Resolve { get; }

	/// <summary>Calls the handler.</summary>
	public WebSocketHandler Handler { get; }

	/// <summary>A display name.</summary>
	public string Name { get; }

	/// <summary>Whether <paramref name="rawPath"/> is this endpoint's path (ASCII case-insensitive, one trailing slash ignored).</summary>
	public bool Matches(ReadOnlySpan<byte> rawPath)
	{
		if (rawPath.Length > 1 && rawPath[^1] == (byte)'/') rawPath = rawPath[..^1];
		return Ascii.EqualsIgnoreCase(rawPath, PathBytes);
	}

	/// <summary>Checks a WebSocket path: starts with <c>/</c>, literal segments only.</summary>
	public static bool IsValidPath(string path, out string? error)
	{
		error = null;
		if (string.IsNullOrEmpty(path) || path[0] != '/')
		{
			error = "it must start with '/'";
			return false;
		}

		if (path.Contains("//", StringComparison.Ordinal))
		{
			error = "it has an empty segment ('//')";
			return false;
		}

		foreach (var c in path)
		{
			if (c is '{' or '}' or '?' or '#' || c <= 0x20 || c >= 0x7F)
			{
				error = "WebSocket paths are literal (no parameters, query or special characters)";
				return false;
			}
		}

		return true;
	}
}

/// <summary>The routes of one assembly: what the routing generator emits and registers with <see cref="WebRoutes"/>.</summary>
public sealed class WebRouteTable(string name, IReadOnlyList<WebRoute> routes, IReadOnlyList<WebSocketRoute> webSockets)
{
	/// <summary>A name (the assembly's).</summary>
	public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

	/// <summary>The HTTP routes.</summary>
	public IReadOnlyList<WebRoute> Routes { get; } = routes ?? throw new ArgumentNullException(nameof(routes));

	/// <summary>The WebSocket endpoints.</summary>
	public IReadOnlyList<WebSocketRoute> WebSockets { get; } = webSockets ?? throw new ArgumentNullException(nameof(webSockets));
}

/// <summary>
/// The route tables of the process. The routing generator registers each assembly's table from a module initializer (so
/// it is present before the assembly's code runs); the web server reads them all when it starts. No reflection.
/// </summary>
public static class WebRoutes
{
	private static readonly List<WebRouteTable> Registered = [];

	/// <summary>Registers <paramref name="table"/> (once per instance).</summary>
	public static void Register(WebRouteTable table)
	{
		ArgumentNullException.ThrowIfNull(table);
		lock (Registered)
		{
			if (!Registered.Contains(table)) Registered.Add(table);
		}
	}

	/// <summary>A copy of the registered tables, in registration order.</summary>
	public static WebRouteTable[] Tables
	{
		get
		{
			lock (Registered) return [.. Registered];
		}
	}
}

/// <summary>
/// Binding helpers the routing generator's code calls: parsing route and query values (percent-decoded, invariant
/// culture), reading bodies, and resolving JSON type information from a source-generated context.
/// </summary>
public static class WebBinding
{
	/// <summary>Parses a raw (still percent-encoded) value as <typeparamref name="T"/> (numbers, <see cref="Guid"/>, ...), invariant culture. Allocation-free.</summary>
	public static bool TryParse<T>(ReadOnlySpan<byte> raw, out T value) where T : IUtf8SpanParsable<T>
	{
		if (raw.IndexOfAny((byte)'%', (byte)'+') < 0) return T.TryParse(raw, CultureInfo.InvariantCulture, out value!);
		Span<byte> decoded = stackalloc byte[Math.Min(raw.Length, 256)];
		if (raw.Length > 256 || !HttpText.TryDecode(raw, decoded, plusIsSpace: true, out var written))
		{
			value = default!;
			return false;
		}

		return T.TryParse(decoded[..written], CultureInfo.InvariantCulture, out value!);
	}

	/// <summary>
	/// Parses a raw value as a boolean: <c>true</c>/<c>false</c>, <c>1</c>/<c>0</c>, <c>on</c>/<c>off</c>, <c>yes</c>/<c>no</c>
	/// (case-insensitive); an empty value (a query key without <c>=</c>) is true.
	/// </summary>
	public static bool TryParseBool(ReadOnlySpan<byte> raw, out bool value)
	{
		if (raw.IsEmpty || Ascii.EqualsIgnoreCase(raw, "true"u8) || raw.SequenceEqual("1"u8) || Ascii.EqualsIgnoreCase(raw, "on"u8) || Ascii.EqualsIgnoreCase(raw, "yes"u8))
		{
			value = true;
			return true;
		}

		value = false;
		return Ascii.EqualsIgnoreCase(raw, "false"u8) || raw.SequenceEqual("0"u8) || Ascii.EqualsIgnoreCase(raw, "off"u8) || Ascii.EqualsIgnoreCase(raw, "no"u8);
	}

	/// <summary>Decodes a raw route value (percent-encoding) to a string.</summary>
	public static string RouteString(ReadOnlySpan<byte> raw) => HttpText.DecodeToString(raw, plusIsSpace: false);

	/// <summary>Decodes a raw query value (percent-encoding, <c>+</c> as a space) to a string.</summary>
	public static string QueryString(ReadOnlySpan<byte> raw) => HttpText.DecodeToString(raw, plusIsSpace: true);

	/// <summary>The body as UTF-8 text.</summary>
	public static string BodyString(in WebRequest request) => Encoding.UTF8.GetString(request.Body);

	/// <summary>Deserializes the body; false with a message when it is not valid JSON of that shape.</summary>
	public static bool TryReadJson<T>(ReadOnlySpan<byte> body, JsonTypeInfo<T> type, out T? value, out string? error)
	{
		ArgumentNullException.ThrowIfNull(type);
		error = null;
		if (body.IsEmpty)
		{
			value = default;
			error = "The request needs a JSON body.";
			return false;
		}

		try
		{
			value = JsonSerializer.Deserialize(body, type);
			return true;
		}
		catch (JsonException ex)
		{
			value = default;
			error = "The body is not valid JSON for " + typeof(T).Name + ": " + ex.Message;
			return false;
		}
	}

	/// <summary>The type information of <typeparamref name="T"/> in <paramref name="context"/> (NativeAOT-safe: a lookup in the generated context).</summary>
	/// <exception cref="InvalidOperationException">The context has no <c>[JsonSerializable(typeof(T))]</c>.</exception>
	public static JsonTypeInfo<T> TypeInfo<T>(JsonSerializerContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		return context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
			?? throw new InvalidOperationException($"The JSON context {context.GetType().Name} has no [JsonSerializable(typeof({typeof(T).Name}))].");
	}
}
