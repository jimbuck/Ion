using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Ion.Extensions.Remote;

/// <summary>
/// A named piece of game state that the remote protocol reads with <c>resources.get</c> and, when writable, writes with
/// <c>resources.set</c> (the Bevy Remote Protocol's resources). Register one with
/// <see cref="RemoteServiceCollectionExtensions.AddRemoteResource"/>; <see cref="Create{T}"/> builds one from a
/// source-generated <see cref="JsonTypeInfo{T}"/>, so it works under NativeAOT.
/// </summary>
public abstract class RemoteResource
{
	/// <summary>Creates a resource.</summary>
	protected RemoteResource(string name, string description)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
		Description = description ?? "";
	}

	/// <summary>The resource name (for example <c>Ion.GameTime</c> or <c>Game.Score</c>).</summary>
	public string Name { get; }

	/// <summary>One sentence describing it.</summary>
	public string Description { get; }

	/// <summary>Whether <c>resources.set</c> may write it (with the mutate scope).</summary>
	public abstract bool CanWrite { get; }

	/// <summary>Reads the value as JSON. Called on the game thread.</summary>
	public abstract JsonNode? Get(IServiceProvider services);

	/// <summary>Writes the value from JSON. Called on the game thread, only when <see cref="CanWrite"/>.</summary>
	public virtual void Set(IServiceProvider services, JsonNode? value) =>
		throw new RemoteException(RemoteErrorCodes.Unsupported, $"The resource '{Name}' is read-only.");

	/// <summary>The JSON Schema of the value, if known.</summary>
	public virtual JsonNode? Schema => null;

	/// <summary>
	/// A resource of type <typeparamref name="T"/> read with <paramref name="get"/> and, when <paramref name="set"/> is
	/// given, written with it. Serialization uses <paramref name="type"/> (from a <c>JsonSerializerContext</c>).
	/// </summary>
	public static RemoteResource Create<T>(string name, string description, JsonTypeInfo<T> type, Func<IServiceProvider, T> get, Action<IServiceProvider, T>? set = null)
	{
		ArgumentNullException.ThrowIfNull(type);
		ArgumentNullException.ThrowIfNull(get);
		return new TypedResource<T>(name, description, type, get, set);
	}

	private sealed class TypedResource<T>(string name, string description, JsonTypeInfo<T> type, Func<IServiceProvider, T> get, Action<IServiceProvider, T>? set)
		: RemoteResource(name, description)
	{
		public override bool CanWrite => set is not null;

		public override JsonNode? Get(IServiceProvider services) => JsonSerializer.SerializeToNode(get(services), type);

		public override void Set(IServiceProvider services, JsonNode? value)
		{
			if (set is null)
			{
				base.Set(services, value);
				return;
			}

			T parsed;
			try
			{
				parsed = value is null ? default! : value.Deserialize(type)!;
			}
			catch (JsonException ex)
			{
				throw RemoteException.InvalidParams($"The value is not a valid {typeof(T).Name}: {ex.Message}");
			}

			set(services, parsed);
		}

		public override JsonNode? Schema => System.Text.Json.Schema.JsonSchemaExporter.GetJsonSchemaAsNode(type);
	}
}

/// <summary>
/// An event type whose payloads <c>events.tail</c> returns (every event type's counts are always listed; payloads only for
/// the registered ones, because events are unmanaged structs serialized through a source-generated
/// <see cref="JsonTypeInfo{T}"/>). Register one with <see cref="RemoteServiceCollectionExtensions.AddRemoteEvent"/>.
/// </summary>
public abstract class RemoteEventSource
{
	/// <summary>Creates a source.</summary>
	protected RemoteEventSource(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
	}

	/// <summary>The event name in <c>events.tail</c> results.</summary>
	public string Name { get; }

	/// <summary>The event type.</summary>
	public abstract Type EventType { get; }

	/// <summary>Creates this source's reader on <paramref name="events"/>. Called once on the game thread.</summary>
	public abstract void Attach(IEvents events);

	/// <summary>Adds the events emitted since the last poll, as JSON, to <paramref name="into"/>. Called on the game thread once per frame.</summary>
	public abstract void Poll(List<JsonNode?> into);

	/// <summary>A source for <typeparamref name="T"/>, serialized with <paramref name="type"/>.</summary>
	public static RemoteEventSource Create<T>(JsonTypeInfo<T> type, string? name = null) where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull(type);
		return new TypedEventSource<T>(name ?? typeof(T).Name, type);
	}

	private sealed class TypedEventSource<T>(string name, JsonTypeInfo<T> type) : RemoteEventSource(name) where T : unmanaged
	{
		private EventReader<T> _reader;
		private bool _attached;

		public override Type EventType => typeof(T);

		[ReadsEvent]
		public override void Attach(IEvents events)
		{
			ArgumentNullException.ThrowIfNull(events);
			_reader = events.Reader<T>();
			_attached = true;
		}

		public override void Poll(List<JsonNode?> into)
		{
			if (!_attached) return;
			foreach (var e in _reader.Read()) into.Add(JsonSerializer.SerializeToNode(e, type));
		}
	}
}
