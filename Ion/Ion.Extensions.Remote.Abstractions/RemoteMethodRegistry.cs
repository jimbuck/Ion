using System.Text.Json.Nodes;

namespace Ion.Extensions.Remote;

/// <summary>
/// Contributes methods to the remote protocol. Register implementations with
/// <see cref="RemoteServiceCollectionExtensions.AddRemoteMethods"/>; the server calls <see cref="Register"/> once, when it
/// starts. This is how modules the remote server does not reference (ECS, UI, physics, the game itself) expose their state:
/// the ECS module registers <c>world.*</c> and <c>registry.schema</c>, a UI module would register <c>ui.tree</c>,
/// <c>ui.click</c> (a mutate method taking a node path), <c>ui.set_value</c>, <c>ui.focus</c> and <c>ui.type</c>.
/// </summary>
public interface IRemoteMethodProvider
{
	/// <summary>Adds this provider's methods to <paramref name="methods"/>.</summary>
	void Register(RemoteMethodRegistry methods);
}

/// <summary>The methods of a remote server, by name.</summary>
public sealed class RemoteMethodRegistry
{
	private readonly List<RemoteMethod> _methods = [];
	private readonly Dictionary<string, RemoteMethod> _byName = new(StringComparer.Ordinal);

	/// <summary>Every method, in registration order.</summary>
	public IReadOnlyList<RemoteMethod> Methods => _methods;

	/// <summary>Adds <paramref name="method"/>.</summary>
	/// <exception cref="InvalidOperationException">A method with the same name is already registered.</exception>
	public RemoteMethodRegistry Add(RemoteMethod method)
	{
		ArgumentNullException.ThrowIfNull(method);
		if (method.Watchable && method.Access != RemoteAccess.Read) throw new ArgumentException($"Only read methods can be watched ('{method.Name}' is a mutate method).", nameof(method));
		if (!_byName.TryAdd(method.Name, method)) throw new InvalidOperationException($"The remote method '{method.Name}' is already registered.");
		_methods.Add(method);
		return this;
	}

	/// <summary>Adds a read method.</summary>
	public RemoteMethodRegistry Read(string name, string description, RemoteHandler handler, JsonObject? parameters = null, bool watchable = false) =>
		Add(new RemoteMethod(name, RemoteAccess.Read, description, handler) { Params = parameters, Watchable = watchable });

	/// <summary>Adds a mutate method.</summary>
	public RemoteMethodRegistry Mutate(string name, string description, RemoteHandler handler, JsonObject? parameters = null) =>
		Add(new RemoteMethod(name, RemoteAccess.Mutate, description, handler) { Params = parameters });

	/// <summary>The method named <paramref name="name"/>, or null.</summary>
	public RemoteMethod? Find(string name) => _byName.GetValueOrDefault(name);
}

/// <summary>
/// Helpers for writing the JSON Schema of a method's parameters (<see cref="RemoteMethod.Params"/>).
/// </summary>
public static class RemoteSchema
{
	/// <summary>An object schema with the given properties; names ending in <c>?</c> are optional.</summary>
	/// <example><c>RemoteSchema.Object(("entity", RemoteSchema.EntityRef), ("components?", RemoteSchema.StringArray))</c></example>
	public static JsonObject Object(params (string Name, JsonNode Schema)[] properties)
	{
		var props = new JsonObject();
		var required = new JsonArray();
		foreach (var (rawName, schema) in properties)
		{
			var optional = rawName.EndsWith('?');
			var name = optional ? rawName[..^1] : rawName;
			props[name] = schema.DeepClone();
			if (!optional) required.Add((JsonNode)name);
		}

		var result = new JsonObject { ["type"] = "object", ["properties"] = props };
		if (required.Count > 0) result["required"] = required;
		return result;
	}

	/// <summary>A schema of the given JSON type with a description.</summary>
	public static JsonObject Of(string type, string description) => new() { ["type"] = type, ["description"] = description };

	/// <summary>A string schema.</summary>
	public static JsonObject String(string description) => Of("string", description);

	/// <summary>An integer schema.</summary>
	public static JsonObject Integer(string description) => Of("integer", description);

	/// <summary>A number schema.</summary>
	public static JsonObject Number(string description) => Of("number", description);

	/// <summary>A boolean schema.</summary>
	public static JsonObject Boolean(string description) => Of("boolean", description);

	/// <summary>An array of strings.</summary>
	public static JsonObject Strings(string description) => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = description };

	/// <summary>Any JSON value.</summary>
	public static JsonObject Any(string description) => new() { ["description"] = description };

	/// <summary>An entity reference: its id (a number) or its <c>EntityName</c> (a string).</summary>
	public static JsonObject EntityRef => new()
	{
		["description"] = "The entity: its id (number, as returned by world.query) or its EntityName (string).",
		["type"] = new JsonArray("integer", "string"),
	};
}
