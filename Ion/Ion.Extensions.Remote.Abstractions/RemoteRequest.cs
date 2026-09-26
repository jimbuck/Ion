using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ion.Extensions.Remote;

/// <summary>
/// One remote call as a handler sees it: the method, its parameters and the application's services. Created by the server
/// on the game thread; the parameter helpers throw <see cref="RemoteException"/> with <see cref="RemoteErrorCodes.InvalidParams"/>
/// so a handler can read what it needs in one line.
/// </summary>
public sealed class RemoteRequest
{
	/// <summary>Creates a request (the server does; tests and providers can too).</summary>
	/// <param name="method">The method name, without the <c>+watch</c> suffix.</param>
	/// <param name="params">The <c>params</c> value (an object, an array, or null).</param>
	/// <param name="services">The application's services.</param>
	/// <param name="access">The access of the caller's session.</param>
	public RemoteRequest(string method, JsonNode? @params, IServiceProvider services, RemoteAccess access = RemoteAccess.Read)
	{
		ArgumentException.ThrowIfNullOrEmpty(method);
		ArgumentNullException.ThrowIfNull(services);
		Method = method;
		Params = @params;
		Services = services;
		Access = access;
	}

	/// <summary>The method name (without <c>+watch</c>).</summary>
	public string Method { get; }

	/// <summary>The <c>params</c> value: normally a <see cref="JsonObject"/>, or null when omitted.</summary>
	public JsonNode? Params { get; }

	/// <summary>The application's services (the root provider).</summary>
	public IServiceProvider Services { get; }

	/// <summary>The access of the caller's session (<see cref="RemoteAccess.Mutate"/> includes read).</summary>
	public RemoteAccess Access { get; }

	/// <summary>Whether this call is a re-evaluation of a watch (the handler should not have side effects either way).</summary>
	public bool IsWatch { get; init; }

	/// <summary>The request id as JSON text (null for notifications and watches' re-evaluations).</summary>
	public string? Id { get; init; }

	/// <summary>The value of parameter <paramref name="name"/>, or null when it is absent or <c>null</c>.</summary>
	public JsonNode? Get(string name) => Params is JsonObject obj && obj.TryGetPropertyValue(name, out var value) ? value : null;

	/// <summary>Whether parameter <paramref name="name"/> is present and not <c>null</c>.</summary>
	public bool Has(string name) => Get(name) is not null;

	/// <summary>A required string parameter.</summary>
	public string GetString(string name) => GetOptionalString(name) ?? throw RemoteException.InvalidParams($"Missing required string parameter '{name}'.");

	/// <summary>An optional string parameter (null when absent).</summary>
	public string? GetOptionalString(string name) => Get(name) switch
	{
		null => null,
		JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
		_ => throw RemoteException.InvalidParams($"Parameter '{name}' must be a string."),
	};

	/// <summary>A required integer parameter.</summary>
	public long GetInt64(string name) => GetOptionalInt64(name) ?? throw RemoteException.InvalidParams($"Missing required integer parameter '{name}'.");

	/// <summary>An optional integer parameter (null when absent).</summary>
	public long? GetOptionalInt64(string name)
	{
		var node = Get(name);
		if (node is null) return null;
		if (node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<long>(out var result)) return result;
		if (node is JsonValue v2 && v2.GetValueKind() == JsonValueKind.Number && v2.TryGetValue<double>(out var d) && d == Math.Floor(d) && Math.Abs(d) < 9e15) return (long)d;
		throw RemoteException.InvalidParams($"Parameter '{name}' must be an integer.");
	}

	/// <summary>An optional integer parameter with a default, checked against a range.</summary>
	public int GetInt32(string name, int defaultValue, int min = int.MinValue, int max = int.MaxValue)
	{
		var value = GetOptionalInt64(name) ?? defaultValue;
		if (value < min || value > max) throw RemoteException.InvalidParams($"Parameter '{name}' must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.");
		return (int)value;
	}

	/// <summary>An optional number parameter (null when absent).</summary>
	public double? GetOptionalDouble(string name)
	{
		var node = Get(name);
		if (node is null) return null;
		if (node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<double>(out var result)) return result;
		throw RemoteException.InvalidParams($"Parameter '{name}' must be a number.");
	}

	/// <summary>An optional boolean parameter with a default.</summary>
	public bool GetBool(string name, bool defaultValue = false) => Get(name) switch
	{
		null => defaultValue,
		JsonValue value when value.GetValueKind() == JsonValueKind.True => true,
		JsonValue value when value.GetValueKind() == JsonValueKind.False => false,
		_ => throw RemoteException.InvalidParams($"Parameter '{name}' must be a boolean."),
	};

	/// <summary>An optional object parameter (null when absent).</summary>
	public JsonObject? GetObject(string name) => Get(name) switch
	{
		null => null,
		JsonObject obj => obj,
		_ => throw RemoteException.InvalidParams($"Parameter '{name}' must be an object."),
	};

	/// <summary>An optional array parameter (null when absent).</summary>
	public JsonArray? GetArray(string name) => Get(name) switch
	{
		null => null,
		JsonArray array => array,
		_ => throw RemoteException.InvalidParams($"Parameter '{name}' must be an array."),
	};

	/// <summary>An optional array of strings (empty when absent).</summary>
	public IReadOnlyList<string> GetStrings(string name)
	{
		var array = GetArray(name);
		if (array is null) return [];
		var result = new List<string>(array.Count);
		foreach (var item in array)
		{
			if (item is JsonValue value && value.GetValueKind() == JsonValueKind.String) result.Add(value.GetValue<string>());
			else throw RemoteException.InvalidParams($"Parameter '{name}' must be an array of strings.");
		}

		return result;
	}

	/// <summary>A service of the application, or a <see cref="RemoteErrorCodes.Unsupported"/> error naming what is missing.</summary>
	public T GetService<T>(string? missing = null) where T : class =>
		Services.GetService(typeof(T)) as T ?? throw new RemoteException(RemoteErrorCodes.Unsupported, missing ?? $"The game has no {typeof(T).Name} service.");
}
