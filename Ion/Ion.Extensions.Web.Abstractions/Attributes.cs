namespace Ion.Extensions.Web;

/// <summary>What an endpoint needs from its caller when the web server has a token (<c>Ion:Web:Token</c>).</summary>
public enum WebAccess
{
	/// <summary><see cref="Read"/> for GET and HEAD, <see cref="Mutate"/> for every other method and for WebSocket endpoints.</summary>
	Auto,

	/// <summary>No token needed: the endpoint only observes the game.</summary>
	Read,

	/// <summary>The bearer token is needed (when one is configured): the endpoint changes the game.</summary>
	Mutate,
}

/// <summary>
/// Declares a method of a system as an HTTP endpoint of the web module (<c>Ion.Extensions.Web</c>). The routing generator
/// turns every such method into an entry of the assembly's static route table (no reflection); the web server calls it
/// on the game thread at the end of a frame (<see cref="StageOrder.Web"/>), with the request parsed and bound.
/// </summary>
/// <remarks>
/// <para>
/// The route is a path template: literal segments, <c>{name}</c> segments bound to the method parameter of that name, and
/// an optional last <c>{*name}</c> that takes the rest of the path. Other parameters are bound from the query string by
/// name, except a <see cref="WebRequest"/> (the request), a <c>ref</c> <see cref="WebResponse"/> (the response to write)
/// and one <see cref="FromBodyAttribute"/> parameter (the body). Route and query parameters may be <c>string</c>,
/// <c>bool</c> or a number type; optional ones are nullable or have a default value.
/// </para>
/// <para>
/// The result is the response: <c>void</c> answers 204 (unless the method writes the response itself), a <c>string</c>
/// is text, a number or <c>bool</c> is JSON, and any other type is serialized with the <c>JsonSerializerContext</c> named
/// by <see cref="Json"/> or by <see cref="WebJsonAttribute"/> on the class or assembly (System.Text.Json source
/// generation, so NativeAOT-safe).
/// </para>
/// <example>
/// <code>
/// [Http("GET", "/score")]
/// public ScoreInfo GetScore() => new(score, balls);
///
/// [Http("POST", "/players/{id}/name")]
/// public void Rename(int id, [FromBody] string name) => players[id].Name = name;
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class HttpAttribute(string method, string route) : Attribute
{
	/// <summary>The HTTP method: <c>GET</c>, <c>HEAD</c>, <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c>.</summary>
	public string Method { get; } = method;

	/// <summary>The path template, starting with <c>/</c>.</summary>
	public string Route { get; } = route;

	/// <summary>Whether the endpoint needs the token (<see cref="WebAccess.Auto"/>: only when it is not a GET or HEAD).</summary>
	public WebAccess Access { get; set; }

	/// <summary>The <c>JsonSerializerContext</c> type for the body and the result (overrides <see cref="WebJsonAttribute"/>).</summary>
	public Type? Json { get; set; }
}

/// <summary>
/// Declares a method of a system as the handler of a WebSocket endpoint: it is called on the game thread (at
/// <see cref="StageOrder.Web"/>) when a client connects, for every message it sends, and when it disconnects, with a
/// <see cref="WebSocketMessage"/> (<c>void OnMessage(in WebSocketMessage message)</c>). Push to the endpoint's clients
/// through <see cref="IWebServer.Channel"/> or reply to one through <see cref="WebSocketMessage.Client"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class WebSocketAttribute(string route) : Attribute
{
	/// <summary>The path (literal segments only).</summary>
	public string Route { get; } = route;

	/// <summary>Whether connecting needs the token (<see cref="WebAccess.Auto"/>: yes, when one is configured).</summary>
	public WebAccess Access { get; set; }
}

/// <summary>Binds a parameter of an <see cref="HttpAttribute"/> method to the request body: <c>string</c>, <c>ReadOnlySpan&lt;byte&gt;</c> (raw), or a JSON type.</summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromBodyAttribute : Attribute;

/// <summary>
/// Names the <c>JsonSerializerContext</c> (a source-generated System.Text.Json context) used for the bodies and results of
/// the <see cref="HttpAttribute"/> methods of a class, or of the whole assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class WebJsonAttribute(Type context) : Attribute
{
	/// <summary>The context type.</summary>
	public Type Context { get; } = context;
}
