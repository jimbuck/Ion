using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>Writes a component to the binary format.</summary>
public delegate void BinaryComponentWriter<T>(BinaryWriter writer, in T value, EntityWriteMap entities);

/// <summary>Reads a component from the binary format.</summary>
public delegate T BinaryComponentReader<T>(BinaryReader reader, EntityReadMap entities);

/// <summary>Maps the entities being saved to their positions in the saved list (entity references are saved as positions).</summary>
public sealed class EntityWriteMap
{
	private readonly Dictionary<Entity, int> _indexes = [];
	private readonly bool _ids;

	internal EntityWriteMap()
	{
	}

	private EntityWriteMap(bool ids) => _ids = ids;

	/// <summary>A map that writes entity references as entity ids (the remote protocol's addressing), not saved positions.</summary>
	internal static EntityWriteMap Ids { get; } = new(ids: true);

	internal void Add(Entity entity) => _indexes[entity] = _indexes.Count;

	/// <summary>The saved position of <paramref name="entity"/>, or -1 when it is not saved (null, dead, or in another world).</summary>
	public int IndexOf(Entity entity) => _ids ? (entity == Entity.Null ? -1 : entity.Id) : _indexes.TryGetValue(entity, out var index) ? index : -1;
}

/// <summary>Maps saved entity positions to the entities created by a load.</summary>
public sealed class EntityReadMap
{
	private readonly World? _world;

	internal EntityReadMap(Entity[] entities) => Entities = entities;

	/// <summary>A map that reads entity references as entity ids of <paramref name="world"/> (the remote protocol's addressing).</summary>
	internal EntityReadMap(World world)
	{
		_world = world;
		Entities = [];
	}

	internal Entity[] Entities { get; }

	/// <summary>The entity created for saved position <paramref name="index"/>, or <see cref="Entity.Null"/> for -1 or an unknown position.</summary>
	public Entity EntityAt(int index)
	{
		if (_world is { } world) return EcsEntities.FindById(world, index);
		return (uint)index < (uint)Entities.Length ? Entities[index] : Entity.Null;
	}
}

/// <summary>Entity lookups by id (the remote protocol addresses entities by id or by name).</summary>
internal static class EcsEntities
{
	private static readonly QueryDescription Everything = new();
	private static readonly QueryDescription Named = new QueryDescription().WithAll<EntityName>();

	/// <summary>The live entity of <paramref name="world"/> with id <paramref name="id"/>, or <see cref="Entity.Null"/>.</summary>
	public static Entity FindById(World world, int id)
	{
		if (id < 0) return Entity.Null;
		foreach (ref var chunk in world.Query(in Everything))
		{
			for (var i = 0; i < chunk.Count; i++)
			{
				var entity = chunk.Entity(i);
				if (entity.Id == id) return entity;
			}
		}

		return Entity.Null;
	}

	/// <summary>The first entity of <paramref name="world"/> named <paramref name="name"/>, or <see cref="Entity.Null"/>.</summary>
	public static Entity FindByName(World world, string name)
	{
		foreach (ref var chunk in world.Query(in Named))
		{
			var names = chunk.GetArray<EntityName>();
			for (var i = 0; i < chunk.Count; i++)
			{
				if (names[i].Value == name) return chunk.Entity(i);
			}
		}

		return Entity.Null;
	}
}

/// <summary>
/// The components the world serializers save, by name (the name is what the files store, so it must stay stable). Each
/// registration has a JSON <see cref="JsonTypeInfo{T}"/> (from a source-generated <c>JsonSerializerContext</c>, so it works
/// under NativeAOT) and a binary reader and writer. <see cref="CreateDefault"/> registers the built-in components.
/// </summary>
public sealed class ComponentSerializerRegistry
{
	private readonly List<ComponentSerializer> _items = [];
	private readonly Dictionary<string, ComponentSerializer> _byName = new(StringComparer.Ordinal);
	private readonly Dictionary<Type, ComponentSerializer> _byType = [];

	/// <summary>The registrations, in registration order.</summary>
	internal IReadOnlyList<ComponentSerializer> Items => _items;

	/// <summary>The registered component names.</summary>
	public IEnumerable<string> Names => _items.Select(i => i.Name);

	/// <summary>
	/// A registry with the built-in components: <see cref="Transform2D"/>, <see cref="Transform"/>, <see cref="Parent"/>,
	/// <see cref="Children"/>, <see cref="EntityName"/>, <see cref="SpriteAnimation"/>, <see cref="Camera2D"/>, and the tags
	/// <see cref="Hidden"/>, <see cref="Visible"/> and <see cref="MainCamera"/>. Computed components (global transforms,
	/// bounds) are not saved, and neither is <see cref="Sprite"/> (it holds a texture; register a codec that saves its
	/// asset path).
	/// </summary>
	public static ComponentSerializerRegistry CreateDefault()
	{
		var registry = new ComponentSerializerRegistry();
		var json = EcsJsonContext.Default;
		registry.AddUnmanaged("Transform2D", json.Transform2D);
		registry.AddUnmanaged("Transform", json.Transform);
		registry.Add("Parent", json.Parent,
			static (BinaryWriter writer, in Parent value, EntityWriteMap entities) => writer.Write(entities.IndexOf(value.Value)),
			static (reader, entities) => new Parent(entities.EntityAt(reader.ReadInt32())));
		registry.Add("Children", json.Children,
			static (BinaryWriter writer, in Children value, EntityWriteMap entities) =>
			{
				var children = value.AsSpan();
				writer.Write(children.Length);
				foreach (var child in children) writer.Write(entities.IndexOf(child));
			},
			static (reader, entities) =>
			{
				var count = reader.ReadInt32();
				var children = new Children();
				for (var i = 0; i < count; i++)
				{
					var child = entities.EntityAt(reader.ReadInt32());
					if (child != Entity.Null) children.Add(child);
				}

				return children;
			});
		registry.Add("EntityName", json.EntityName,
			static (BinaryWriter writer, in EntityName value, EntityWriteMap _) => writer.Write(value.Value ?? ""),
			static (reader, _) => new EntityName(reader.ReadString()));
		registry.Add("SpriteAnimation", json.SpriteAnimation,
			static (BinaryWriter writer, in SpriteAnimation value, EntityWriteMap _) =>
			{
				var frames = value.Frames ?? [];
				writer.Write(frames.Length);
				foreach (var frame in frames)
				{
					writer.Write(frame.X);
					writer.Write(frame.Y);
					writer.Write(frame.Width);
					writer.Write(frame.Height);
				}

				writer.Write(value.FramesPerSecond);
				writer.Write(value.Loop);
				writer.Write(value.Paused);
				writer.Write(value.Time);
				writer.Write(value.Frame);
			},
			static (reader, _) =>
			{
				var frames = new RectangleF[reader.ReadInt32()];
				for (var i = 0; i < frames.Length; i++) frames[i] = new RectangleF(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
				return new SpriteAnimation(frames, reader.ReadSingle(), reader.ReadBoolean()) { Paused = reader.ReadBoolean(), Time = reader.ReadSingle(), Frame = reader.ReadInt32() };
			});
		registry.Add("Camera2D", json.Camera2D,
			static (BinaryWriter writer, in Camera2D value, EntityWriteMap _) =>
			{
				writer.Write(value.Position.X);
				writer.Write(value.Position.Y);
				writer.Write(value.Zoom);
				writer.Write(value.Rotation);
			},
			static (reader, _) => new Camera2D { Position = new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle()), Zoom = reader.ReadSingle(), Rotation = reader.ReadSingle() });
		registry.AddTag<Hidden>("Hidden");
		registry.AddTag<Visible>("Visible");
		registry.AddTag<MainCamera>("MainCamera");
		return registry;
	}

	/// <summary>Registers <typeparamref name="T"/> under <paramref name="name"/> with a JSON type info and binary codecs.</summary>
	public ComponentSerializerRegistry Add<T>(string name, JsonTypeInfo<T> json, BinaryComponentWriter<T> write, BinaryComponentReader<T> read)
	{
		ArgumentNullException.ThrowIfNull(json);
		ArgumentNullException.ThrowIfNull(write);
		ArgumentNullException.ThrowIfNull(read);
		return Add(new ComponentSerializer<T>(name, json, write, read));
	}

	/// <summary>
	/// Registers an unmanaged <typeparamref name="T"/> whose binary form is its memory (little-endian, as on every platform
	/// Ion targets). It must not hold <see cref="Entity"/> references (they would not be remapped); use
	/// <see cref="Add{T}"/> with codecs for those.
	/// </summary>
	public ComponentSerializerRegistry AddUnmanaged<T>(string name, JsonTypeInfo<T> json) where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull(json);
		return Add(new ComponentSerializer<T>(name, json,
			static (BinaryWriter writer, in T value, EntityWriteMap _) => writer.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value))),
			static (reader, _) =>
			{
				T value = default;
				reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(new Span<T>(ref value)));
				return value;
			}));
	}

	/// <summary>Registers a tag component (no data) under <paramref name="name"/>.</summary>
	public ComponentSerializerRegistry AddTag<T>(string name) where T : struct => Add(new TagSerializer<T>(name));

	private ComponentSerializerRegistry Add(ComponentSerializer serializer)
	{
		ArgumentException.ThrowIfNullOrEmpty(serializer.Name);
		if (!_byName.TryAdd(serializer.Name, serializer)) throw new InvalidOperationException($"A component is already registered as '{serializer.Name}'.");
		if (!_byType.TryAdd(serializer.ComponentType.Type, serializer))
		{
			_byName.Remove(serializer.Name);
			throw new InvalidOperationException($"{serializer.ComponentType.Type.Name} is already registered as '{_byType[serializer.ComponentType.Type].Name}'.");
		}

		_items.Add(serializer);
		return this;
	}

	internal ComponentSerializer? Find(string name) => _byName.GetValueOrDefault(name);

	internal ComponentSerializer? Find(ComponentType type) => _byType.GetValueOrDefault(type.Type);
}

/// <summary>Reads and writes one component type for the world serializers.</summary>
internal abstract class ComponentSerializer(string name, ComponentType componentType)
{
	public string Name { get; } = name;

	public ComponentType ComponentType { get; } = componentType;

	public abstract void WriteJson(Utf8JsonWriter writer, ref Chunk chunk, int index, JsonSerializerOptions options);

	public abstract void ReadJson(JsonElement element, World world, Entity entity, JsonSerializerOptions options);

	public abstract void WriteBinary(BinaryWriter writer, ref Chunk chunk, int index, EntityWriteMap entities);

	public abstract void ReadBinary(BinaryReader reader, World world, Entity entity, EntityReadMap entities);

	/// <summary>Whether <paramref name="entity"/> has this component.</summary>
	public bool Has(World world, Entity entity) => world.Has(entity, ComponentType);

	/// <summary>Writes the component of one entity (the remote protocol's reads).</summary>
	public abstract void WriteJson(Utf8JsonWriter writer, World world, Entity entity);

	/// <summary>Adds the component to <paramref name="entity"/> from JSON, or replaces it when present (the remote protocol's writes).</summary>
	public abstract void Insert(JsonElement element, World world, Entity entity);

	/// <summary>Removes the component from <paramref name="entity"/> (nothing when absent).</summary>
	public void Remove(World world, Entity entity)
	{
		if (world.Has(entity, ComponentType)) world.Remove(entity, ComponentType);
	}

	/// <summary>The JSON Schema of the component's JSON form.</summary>
	public abstract JsonNode Schema();

	/// <summary>Whether the component is a tag (no data).</summary>
	public virtual bool IsTag => false;
}

internal sealed class ComponentSerializer<T>(string name, JsonTypeInfo<T> json, BinaryComponentWriter<T> write, BinaryComponentReader<T> read)
	: ComponentSerializer(name, Component<T>.ComponentType)
{
	public override void WriteJson(Utf8JsonWriter writer, World world, Entity entity) => JsonSerializer.Serialize(writer, world.Get<T>(entity), json);

	public override void Insert(JsonElement element, World world, Entity entity)
	{
		var value = element.Deserialize(json)!;
		if (world.Has<T>(entity)) world.Set(entity, value);
		else world.Add(entity, value);
	}

	public override JsonNode Schema() => System.Text.Json.Schema.JsonSchemaExporter.GetJsonSchemaAsNode(json);

	public override void WriteJson(Utf8JsonWriter writer, ref Chunk chunk, int index, JsonSerializerOptions options) =>
		JsonSerializer.Serialize(writer, chunk.GetArray<T>()[index], json);

	public override void ReadJson(JsonElement element, World world, Entity entity, JsonSerializerOptions options)
	{
		var value = element.Deserialize(json)!;
		world.Set(entity, value);
	}

	public override void WriteBinary(BinaryWriter writer, ref Chunk chunk, int index, EntityWriteMap entities) => write(writer, in chunk.GetArray<T>()[index], entities);

	public override void ReadBinary(BinaryReader reader, World world, Entity entity, EntityReadMap entities) => world.Set(entity, read(reader, entities));
}

internal sealed class TagSerializer<T>(string name) : ComponentSerializer(name, Component<T>.ComponentType) where T : struct
{
	public override bool IsTag => true;

	public override void WriteJson(Utf8JsonWriter writer, World world, Entity entity)
	{
		writer.WriteStartObject();
		writer.WriteEndObject();
	}

	public override void Insert(JsonElement element, World world, Entity entity)
	{
		if (!world.Has<T>(entity)) world.Add<T>(entity);
	}

	public override JsonNode Schema() => new JsonObject { ["type"] = "object", ["description"] = "A tag (no data)." };

	public override void WriteJson(Utf8JsonWriter writer, ref Chunk chunk, int index, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteEndObject();
	}

	public override void ReadJson(JsonElement element, World world, Entity entity, JsonSerializerOptions options)
	{
	}

	public override void WriteBinary(BinaryWriter writer, ref Chunk chunk, int index, EntityWriteMap entities)
	{
	}

	public override void ReadBinary(BinaryReader reader, World world, Entity entity, EntityReadMap entities)
	{
	}
}
