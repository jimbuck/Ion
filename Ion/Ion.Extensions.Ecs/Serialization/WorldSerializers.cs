using System.Text;
using System.Text.Json;

using Arch.Core;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Saves a world as JSON: <c>{"format":"ion-world","version":1,"entities":[{"Transform2D":{...},"Parent":0},...]}</c>,
/// one object per entity with its registered components by name (see <see cref="ComponentSerializerRegistry"/>).
/// Entities are saved in query order; entity references are saved as positions in that list.
/// </summary>
/// <remarks>
/// Ion does not use <c>Arch.Persistence</c> here: its 2.0.0 package is compiled against Arch 2.0 and fails to load
/// against Arch 2.1 (<c>TypeLoadException</c> on <c>Arch.Core.Utils.ComponentType</c>), depends on a MessagePack prerelease
/// with a known vulnerability (NU1902) and on Utf8Json, which emits IL at run time (no NativeAOT). The format here is
/// Ion's own, with System.Text.Json source-generated metadata.
/// </remarks>
public sealed class JsonWorldSerializer(ComponentSerializerRegistry components) : IWorldSerializer
{
	private const string Format = "ion-world";
	private const int Version = 1;
	private static readonly QueryDescription Everything = new();

	/// <summary>A serializer of the built-in components.</summary>
	public JsonWorldSerializer() : this(ComponentSerializerRegistry.CreateDefault())
	{
	}

	/// <summary>Whether the JSON is indented.</summary>
	public bool Indented { get; init; }

	/// <inheritdoc/>
	public void Serialize(World world, Stream stream)
	{
		ArgumentNullException.ThrowIfNull(world);
		ArgumentNullException.ThrowIfNull(stream);

		var map = WorldSerialization.MapEntities(world, Everything);
		var options = EcsJsonContext.Default.Options;
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = Indented });

		EntityJson.Write = map;
		try
		{
			writer.WriteStartObject();
			writer.WriteString("format", Format);
			writer.WriteNumber("version", Version);
			writer.WriteStartArray("entities");

			var present = new List<ComponentSerializer>();
			foreach (ref var chunk in world.Query(in Everything))
			{
				WorldSerialization.Present(components, ref chunk, present);
				for (var i = 0; i < chunk.Count; i++)
				{
					writer.WriteStartObject();
					foreach (var serializer in present)
					{
						writer.WritePropertyName(serializer.Name);
						serializer.WriteJson(writer, ref chunk, i, options);
					}

					writer.WriteEndObject();
				}
			}

			writer.WriteEndArray();
			writer.WriteEndObject();
		}
		finally
		{
			EntityJson.Write = null;
		}
	}

	/// <inheritdoc/>
	public IReadOnlyList<Entity> Deserialize(Stream stream, World world)
	{
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentNullException.ThrowIfNull(world);

		using var document = JsonDocument.Parse(stream);
		var root = document.RootElement;
		if (!root.TryGetProperty("format", out var format) || format.GetString() != Format) throw new InvalidDataException("Not an Ion world file (format is not \"ion-world\").");
		if (root.GetProperty("version").GetInt32() != Version) throw new InvalidDataException($"Unsupported Ion world version {root.GetProperty("version").GetInt32()}.");

		var saved = root.GetProperty("entities");
		var created = new Entity[saved.GetArrayLength()];
		var types = new List<ComponentType>();
		var index = 0;
		foreach (var entity in saved.EnumerateArray())
		{
			types.Clear();
			foreach (var property in entity.EnumerateObject())
			{
				if (components.Find(property.Name) is { } serializer) types.Add(serializer.ComponentType);
			}

			created[index++] = world.Create([.. types]);
		}

		var options = EcsJsonContext.Default.Options;
		EntityJson.Read = new EntityReadMap(created);
		try
		{
			index = 0;
			foreach (var entity in saved.EnumerateArray())
			{
				var target = created[index++];
				foreach (var property in entity.EnumerateObject())
				{
					components.Find(property.Name)?.ReadJson(property.Value, world, target, options);
				}
			}
		}
		finally
		{
			EntityJson.Read = null;
		}

		return created;
	}

	/// <summary>The world as a JSON string.</summary>
	public string ToJson(World world)
	{
		using var stream = new MemoryStream();
		Serialize(world, stream);
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}

	/// <summary>Creates the entities of <paramref name="json"/> in <paramref name="world"/>.</summary>
	public IReadOnlyList<Entity> FromJson(string json, World world)
	{
		ArgumentNullException.ThrowIfNull(json);
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		return Deserialize(stream, world);
	}
}

/// <summary>
/// Saves a world in a compact binary form: a header (<c>IONW</c>, version), the table of component names, then per entity
/// its components as (table index, byte length, payload), so a reader skips components it does not know. Payloads are
/// written by the registrations' binary codecs (see <see cref="ComponentSerializerRegistry"/>).
/// </summary>
public sealed class BinaryWorldSerializer(ComponentSerializerRegistry components) : IWorldSerializer
{
	private static ReadOnlySpan<byte> Magic => "IONW"u8;
	private const int Version = 1;
	private static readonly QueryDescription Everything = new();

	/// <summary>A serializer of the built-in components.</summary>
	public BinaryWorldSerializer() : this(ComponentSerializerRegistry.CreateDefault())
	{
	}

	/// <inheritdoc/>
	public void Serialize(World world, Stream stream)
	{
		ArgumentNullException.ThrowIfNull(world);
		ArgumentNullException.ThrowIfNull(stream);

		var map = WorldSerialization.MapEntities(world, Everything);
		using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
		using var payload = new MemoryStream();
		using var payloadWriter = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);

		writer.Write(Magic);
		writer.Write(Version);
		var table = components.Items;
		writer.Write(table.Count);
		foreach (var serializer in table) writer.Write(serializer.Name);

		writer.Write(world.Size);
		var present = new List<ComponentSerializer>();
		foreach (ref var chunk in world.Query(in Everything))
		{
			WorldSerialization.Present(components, ref chunk, present);
			for (var i = 0; i < chunk.Count; i++)
			{
				writer.Write(present.Count);
				foreach (var serializer in present)
				{
					payload.SetLength(0);
					serializer.WriteBinary(payloadWriter, ref chunk, i, map);
					payloadWriter.Flush();
					writer.Write(IndexOf(table, serializer));
					writer.Write((int)payload.Length);
					writer.Write(payload.GetBuffer(), 0, (int)payload.Length);
				}
			}
		}
	}

	/// <inheritdoc/>
	public IReadOnlyList<Entity> Deserialize(Stream stream, World world)
	{
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentNullException.ThrowIfNull(world);

		using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
		Span<byte> magic = stackalloc byte[4];
		reader.BaseStream.ReadExactly(magic);
		if (!magic.SequenceEqual(Magic)) throw new InvalidDataException("Not an Ion world file (no IONW header).");
		var version = reader.ReadInt32();
		if (version != Version) throw new InvalidDataException($"Unsupported Ion world version {version}.");

		var table = new ComponentSerializer?[reader.ReadInt32()];
		for (var t = 0; t < table.Length; t++) table[t] = components.Find(reader.ReadString());

		// First the entities with their component types (so references between them resolve), then the values.
		var count = reader.ReadInt32();
		var saved = new List<(ComponentSerializer Serializer, byte[] Payload)>[count];
		var created = new Entity[count];
		var types = new List<ComponentType>();
		for (var e = 0; e < count; e++)
		{
			var componentCount = reader.ReadInt32();
			var list = new List<(ComponentSerializer, byte[])>(componentCount);
			types.Clear();
			for (var c = 0; c < componentCount; c++)
			{
				var serializer = table[reader.ReadInt32()];
				var bytes = reader.ReadBytes(reader.ReadInt32());
				if (serializer is null) continue;
				list.Add((serializer, bytes));
				types.Add(serializer.ComponentType);
			}

			saved[e] = list;
			created[e] = world.Create([.. types]);
		}

		var map = new EntityReadMap(created);
		for (var e = 0; e < count; e++)
		{
			foreach (var (serializer, bytes) in saved[e])
			{
				using var payload = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
				serializer.ReadBinary(payload, world, created[e], map);
			}
		}

		return created;
	}

	/// <summary>The world as bytes.</summary>
	public byte[] ToBytes(World world)
	{
		using var stream = new MemoryStream();
		Serialize(world, stream);
		return stream.ToArray();
	}

	/// <summary>Creates the entities of <paramref name="bytes"/> in <paramref name="world"/>.</summary>
	public IReadOnlyList<Entity> FromBytes(byte[] bytes, World world)
	{
		ArgumentNullException.ThrowIfNull(bytes);
		using var stream = new MemoryStream(bytes);
		return Deserialize(stream, world);
	}

	private static int IndexOf(IReadOnlyList<ComponentSerializer> table, ComponentSerializer serializer)
	{
		for (var i = 0; i < table.Count; i++)
		{
			if (ReferenceEquals(table[i], serializer)) return i;
		}

		return -1;
	}
}

internal static class WorldSerialization
{
	public static EntityWriteMap MapEntities(World world, in QueryDescription everything)
	{
		var map = new EntityWriteMap();
		foreach (ref var chunk in world.Query(in everything))
		{
			for (var i = 0; i < chunk.Count; i++) map.Add(chunk.Entity(i));
		}

		return map;
	}

	public static void Present(ComponentSerializerRegistry components, ref Chunk chunk, List<ComponentSerializer> present)
	{
		present.Clear();
		foreach (var serializer in components.Items)
		{
			if (chunk.Has(serializer.ComponentType)) present.Add(serializer);
		}
	}
}
