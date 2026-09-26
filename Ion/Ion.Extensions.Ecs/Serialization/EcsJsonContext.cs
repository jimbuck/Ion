using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>Source-generated JSON metadata of the built-in components (NativeAOT-safe).</summary>
[JsonSourceGenerationOptions(
	IncludeFields = true,
	Converters = [typeof(EntityJsonConverter), typeof(ChildrenJsonConverter), typeof(Vector2JsonConverter), typeof(Vector3JsonConverter), typeof(QuaternionJsonConverter), typeof(RectangleFJsonConverter), typeof(TransformJsonConverter)])]
[JsonSerializable(typeof(Transform2D))]
[JsonSerializable(typeof(Transform))]
[JsonSerializable(typeof(Parent))]
[JsonSerializable(typeof(Children))]
[JsonSerializable(typeof(EntityName))]
[JsonSerializable(typeof(SpriteAnimation))]
[JsonSerializable(typeof(Camera2D))]
internal sealed partial class EcsJsonContext : JsonSerializerContext
{
}

/// <summary>The entity maps of the serialization running on this thread (entity references are saved as positions).</summary>
internal static class EntityJson
{
	[ThreadStatic] public static EntityWriteMap? Write;
	[ThreadStatic] public static EntityReadMap? Read;
}

/// <summary>
/// Writes an <see cref="Entity"/> as its position in the saved entity list (-1 when it is not saved) and reads it back
/// as the entity created for that position. Use it in your own <see cref="JsonSerializerContext"/> for components that
/// reference entities.
/// </summary>
public sealed class EntityJsonConverter : JsonConverter<Entity>
{
	/// <inheritdoc/>
	public override Entity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		EntityJson.Read?.EntityAt(reader.GetInt32()) ?? Entity.Null;

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, Entity value, JsonSerializerOptions options) =>
		writer.WriteNumberValue(EntityJson.Write?.IndexOf(value) ?? -1);
}

internal sealed class ChildrenJsonConverter : JsonConverter<Children>
{
	public override Children Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var children = new Children();
		if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Children is an array of entity positions.");
		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
		{
			var child = EntityJson.Read?.EntityAt(reader.GetInt32()) ?? Entity.Null;
			if (child != Entity.Null) children.Add(child);
		}

		return children;
	}

	public override void Write(Utf8JsonWriter writer, Children value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		foreach (var child in value.AsSpan()) writer.WriteNumberValue(EntityJson.Write?.IndexOf(child) ?? -1);
		writer.WriteEndArray();
	}
}

internal sealed class Vector2JsonConverter : JsonConverter<Vector2>
{
	public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		Span<float> v = stackalloc float[2];
		JsonFloats.Read(ref reader, v);
		return new Vector2(v[0], v[1]);
	}

	public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options) => JsonFloats.Write(writer, [value.X, value.Y]);
}

internal sealed class Vector3JsonConverter : JsonConverter<Vector3>
{
	public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		Span<float> v = stackalloc float[3];
		JsonFloats.Read(ref reader, v);
		return new Vector3(v[0], v[1], v[2]);
	}

	public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) => JsonFloats.Write(writer, [value.X, value.Y, value.Z]);
}

internal sealed class QuaternionJsonConverter : JsonConverter<Quaternion>
{
	public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		Span<float> v = stackalloc float[4];
		JsonFloats.Read(ref reader, v);
		return new Quaternion(v[0], v[1], v[2], v[3]);
	}

	public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options) => JsonFloats.Write(writer, [value.X, value.Y, value.Z, value.W]);
}

internal sealed class RectangleFJsonConverter : JsonConverter<RectangleF>
{
	public override RectangleF Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		Span<float> v = stackalloc float[4];
		JsonFloats.Read(ref reader, v);
		return new RectangleF(v[0], v[1], v[2], v[3]);
	}

	public override void Write(Utf8JsonWriter writer, RectangleF value, JsonSerializerOptions options) => JsonFloats.Write(writer, [value.X, value.Y, value.Width, value.Height]);
}

internal static class JsonFloats
{
	public static void Read(ref Utf8JsonReader reader, scoped Span<float> values)
	{
		if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException($"Expected an array of {values.Length} numbers.");
		for (var i = 0; i < values.Length; i++)
		{
			if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException($"Expected an array of {values.Length} numbers.");
			values[i] = reader.GetSingle();
		}

		if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException($"Expected an array of {values.Length} numbers.");
	}

	public static void Write(Utf8JsonWriter writer, ReadOnlySpan<float> values)
	{
		writer.WriteStartArray();
		foreach (var value in values) writer.WriteNumberValue(value);
		writer.WriteEndArray();
	}
}

/// <summary>Writes a 3D <see cref="Transform"/> as its position, rotation and scale (not its computed directions).</summary>
internal sealed class TransformJsonConverter : JsonConverter<Transform>
{
	public override Transform Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Transform is an object.");
		var result = new Transform();
		Span<float> v = stackalloc float[4];
		while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
		{
			var name = reader.GetString();
			reader.Read();
			switch (name)
			{
				case "Position": JsonFloats.Read(ref reader, v[..3]); result.Position = new Vector3(v[0], v[1], v[2]); break;
				case "Rotation": JsonFloats.Read(ref reader, v); result.Rotation = new Quaternion(v[0], v[1], v[2], v[3]); break;
				case "Scale": JsonFloats.Read(ref reader, v[..3]); result.Scale = new Vector3(v[0], v[1], v[2]); break;
				default: reader.Skip(); break;
			}
		}

		return result;
	}

	public override void Write(Utf8JsonWriter writer, Transform value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WritePropertyName("Position");
		JsonFloats.Write(writer, [value.Position.X, value.Position.Y, value.Position.Z]);
		writer.WritePropertyName("Rotation");
		JsonFloats.Write(writer, [value.Rotation.X, value.Rotation.Y, value.Rotation.Z, value.Rotation.W]);
		writer.WritePropertyName("Scale");
		JsonFloats.Write(writer, [value.Scale.X, value.Scale.Y, value.Scale.Z]);
		writer.WriteEndObject();
	}
}
