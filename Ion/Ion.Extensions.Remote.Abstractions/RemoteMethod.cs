using System.Text.Json.Nodes;

namespace Ion.Extensions.Remote;

/// <summary>
/// What a remote method needs from the caller's session: <see cref="Read"/> methods observe the game, <see cref="Mutate"/>
/// methods change it (components, entities, resources, input, pausing). The mutate scope is granted only when the game was
/// started with <c>--remote-allow-mutations</c> (<c>Ion:Remote:AllowMutations = true</c>), and mutations are applied on the
/// game thread at a stage boundary, idempotently per request id, and never while a scene is loading.
/// </summary>
public enum RemoteAccess
{
	/// <summary>Observes the game: queries, reads, schema, metrics, screenshots, watches.</summary>
	Read,

	/// <summary>Changes the game: component writes, spawn and despawn, resource writes, input, pause and step.</summary>
	Mutate,
}

/// <summary>
/// Handles one remote request on the game thread. Returns the JSON-RPC <c>result</c> (null for <c>null</c>), or throws
/// <see cref="RemoteException"/> for a protocol error (any other exception becomes an internal error with its message).
/// </summary>
public delegate JsonNode? RemoteHandler(RemoteRequest request);

/// <summary>
/// A JSON-RPC method of the remote protocol: its name (dotted, lower case, <c>module.verb</c>, for example
/// <c>world.query</c> or <c>ui.click</c>), the access it needs, and its handler.
/// </summary>
public sealed class RemoteMethod
{
	/// <summary>Creates a method.</summary>
	/// <param name="name">The method name; must not contain <c>+</c> (the watch suffix is added by the server).</param>
	/// <param name="access">The access it needs.</param>
	/// <param name="description">One sentence for <c>rpc.discover</c> and the MCP tool list.</param>
	/// <param name="handler">The handler, called on the game thread.</param>
	public RemoteMethod(string name, RemoteAccess access, string description, RemoteHandler handler)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (name.Contains('+', StringComparison.Ordinal)) throw new ArgumentException("A method name cannot contain '+'; set Watchable instead of registering a '+watch' variant.", nameof(name));
		ArgumentNullException.ThrowIfNull(description);
		ArgumentNullException.ThrowIfNull(handler);
		Name = name;
		Access = access;
		Description = description;
		Handler = handler;
	}

	/// <summary>The method name.</summary>
	public string Name { get; }

	/// <summary>The access it needs.</summary>
	public RemoteAccess Access { get; }

	/// <summary>One sentence describing it.</summary>
	public string Description { get; }

	/// <summary>The handler.</summary>
	public RemoteHandler Handler { get; }

	/// <summary>
	/// The JSON Schema of the <c>params</c> object (an object schema with <c>properties</c> and <c>required</c>), for
	/// <c>rpc.discover</c> and tool generation. Null means "no parameters documented".
	/// </summary>
	public JsonObject? Params { get; init; }

	/// <summary>
	/// Whether the method has a <c>+watch</c> variant (<c>name+watch</c>), over the WebSocket and stdio transports: the
	/// server calls the handler again at the end of every frame and sends the result, with the watch request's id, each
	/// time it differs from the last one sent. Only read methods can be watched.
	/// </summary>
	public bool Watchable { get; init; }
}
