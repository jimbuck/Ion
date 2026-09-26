using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ion.Tools;

/// <summary>The token file a game writes when its remote server starts (see the remote module's <c>RemoteEndpointInfo</c>).</summary>
public sealed class RemoteEndpoint
{
	/// <summary>The format version.</summary>
	public int Version { get; set; }

	/// <summary>The game's process id.</summary>
	public int Pid { get; set; }

	/// <summary>The game's title.</summary>
	public string? Title { get; set; }

	/// <summary>The HTTP JSON-RPC endpoint.</summary>
	public string? Url { get; set; }

	/// <summary>The WebSocket endpoint.</summary>
	public string? WebSocketUrl { get; set; }

	/// <summary>The read token.</summary>
	public string? ReadToken { get; set; }

	/// <summary>The mutate token, when mutations are allowed.</summary>
	public string? MutateToken { get; set; }

	/// <summary>Whether mutations are allowed.</summary>
	public bool AllowMutations { get; set; }

	/// <summary>Reads a token file.</summary>
	public static RemoteEndpoint Read(string path) =>
		JsonSerializer.Deserialize(File.ReadAllText(path), ToolJsonContext.Default.RemoteEndpoint) ?? throw new InvalidDataException($"'{path}' is not an Ion remote token file.");
}

/// <summary>Source-generated JSON metadata of the tools' typed documents.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RemoteEndpoint))]
internal sealed partial class ToolJsonContext : JsonSerializerContext
{
}

/// <summary>A JSON-RPC error returned by the game.</summary>
public sealed class RemoteCallException(int code, string message, JsonNode? data) : Exception(message)
{
	/// <summary>The JSON-RPC error code.</summary>
	public int Code { get; } = code;

	/// <summary>The error data.</summary>
	public new JsonNode? Data { get; } = data;
}

/// <summary>
/// A client of a running game's remote protocol over HTTP: one JSON-RPC request per POST, with the bearer token from the
/// game's token file (the mutate token when there is one). Request ids are unique, so a retried request is recognized by
/// the game and a mutation is never applied twice.
/// </summary>
public sealed class RemoteClient : IDisposable
{
	private readonly HttpClient _http;

	/// <summary>Connects to <paramref name="url"/> with <paramref name="token"/>.</summary>
	public RemoteClient(Uri url, string token, TimeSpan? timeout = null)
	{
		ArgumentNullException.ThrowIfNull(url);
		ArgumentException.ThrowIfNullOrEmpty(token);
		Url = url;
		_http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(60) };
		_http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
	}

	/// <summary>The endpoint.</summary>
	public Uri Url { get; }

	/// <summary>The endpoint description, when created from a token file.</summary>
	public RemoteEndpoint? Endpoint { get; private init; }

	/// <summary>Connects with a token file (the mutate token when present, else the read token).</summary>
	public static RemoteClient FromTokenFile(string path, TimeSpan? timeout = null)
	{
		var endpoint = RemoteEndpoint.Read(path);
		var token = endpoint.MutateToken ?? endpoint.ReadToken ?? throw new InvalidDataException($"The token file '{path}' has no token.");
		var url = endpoint.Url ?? throw new InvalidDataException($"The token file '{path}' has no HTTP endpoint (is the game using the stdio transport only?).");
		return new RemoteClient(new Uri(url), token, timeout) { Endpoint = endpoint };
	}

	/// <summary>Sends a request and returns the whole response object (<c>result</c> or <c>error</c>).</summary>
	public JsonObject Send(string method, JsonObject? parameters = null, string? id = null)
	{
		var request = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"] = id ?? $"ion-{Guid.NewGuid():N}",
			["method"] = method,
		};
		if (parameters is not null) request["params"] = parameters.DeepClone();

		using var message = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json") };
		using var response = _http.Send(message);
		using var stream = response.Content.ReadAsStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		var text = reader.ReadToEnd();
		if (text.Length == 0) throw new RemoteCallException(-32603, $"HTTP {(int)response.StatusCode} with an empty body.", null);
		return JsonNode.Parse(text)?.AsObject() ?? throw new RemoteCallException(-32700, "Empty response.", null);
	}

	/// <summary>Calls <paramref name="method"/> and returns its result.</summary>
	/// <exception cref="RemoteCallException">The game answered with an error.</exception>
	public JsonNode? Call(string method, JsonObject? parameters = null)
	{
		var response = Send(method, parameters);
		if (response["error"] is JsonObject error)
		{
			throw new RemoteCallException(error["code"]?.GetValue<int>() ?? -32603, error["message"]?.GetValue<string>() ?? "Unknown error.", error["data"]?.DeepClone());
		}

		return response["result"]?.DeepClone();
	}

	/// <inheritdoc/>
	public void Dispose() => _http.Dispose();
}
