using System.Text.Json.Nodes;

namespace Ion.Extensions.Remote;

/// <summary>
/// The JSON-RPC error codes of the remote protocol: the standard ones (-32700 to -32603) and Ion's (-32001 to -32099).
/// </summary>
public static class RemoteErrorCodes
{
	/// <summary>The request is not valid JSON.</summary>
	public const int ParseError = -32700;

	/// <summary>The JSON is not a valid JSON-RPC 2.0 request.</summary>
	public const int InvalidRequest = -32600;

	/// <summary>No such method.</summary>
	public const int MethodNotFound = -32601;

	/// <summary>The parameters are missing or of the wrong type.</summary>
	public const int InvalidParams = -32602;

	/// <summary>The handler failed (the message is the exception's).</summary>
	public const int InternalError = -32603;

	/// <summary>No bearer token, or an unknown one (HTTP 401). Stdio sessions never get it.</summary>
	public const int Unauthorized = -32001;

	/// <summary>The session's token lacks the mutate scope, or the request came from a disallowed origin (HTTP 403).</summary>
	public const int Forbidden = -32002;

	/// <summary>A mutation arrived while a scene is loading; retry on a later frame (with the same id).</summary>
	public const int SceneLoading = -32003;

	/// <summary>The entity, component, resource or node the request names does not exist.</summary>
	public const int NotFound = -32004;

	/// <summary>The game cannot do this (no ECS world, no screenshot source, a read-only resource, watch over plain HTTP).</summary>
	public const int Unsupported = -32005;

	/// <summary>The game thread did not answer in time (the loop is not running, or a frame took too long).</summary>
	public const int Timeout = -32006;
}

/// <summary>
/// A JSON-RPC error raised by a remote handler: the server answers with <see cref="Code"/>, the message and <see cref="Data"/>.
/// </summary>
public sealed class RemoteException : Exception
{
	/// <summary>Creates an error.</summary>
	public RemoteException(int code, string message, JsonNode? data = null) : base(message)
	{
		Code = code;
		Data = data;
	}

	/// <summary>The JSON-RPC error code (see <see cref="RemoteErrorCodes"/>).</summary>
	public int Code { get; }

	/// <summary>Optional structured details, sent as the error's <c>data</c>.</summary>
	public new JsonNode? Data { get; }

	/// <summary>An <see cref="RemoteErrorCodes.InvalidParams"/> error.</summary>
	public static RemoteException InvalidParams(string message) => new(RemoteErrorCodes.InvalidParams, message);

	/// <summary>A <see cref="RemoteErrorCodes.NotFound"/> error.</summary>
	public static RemoteException NotFound(string message) => new(RemoteErrorCodes.NotFound, message);
}
