using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Names for the renderer handles the 3D components hold (<see cref="MeshHandle"/>, <see cref="MaterialHandle"/>,
/// <see cref="TextureHandle"/>, <see cref="RenderTargetHandle"/>), so a saved world can store a name instead of a run-time
/// id and a load can find the handle again. Name the meshes, materials and textures you create, and add the models you
/// load (<see cref="AddModel"/>), before saving or loading; the same names must exist when loading.
/// </summary>
/// <remarks>
/// A handle without a name is saved as <c>#id</c>, its run-time id: that loads back as the same handle in the same
/// process (undo, the remote protocol, tests), but means nothing after a restart. <see cref="MeshHandle.None"/> (and the
/// other <c>None</c>s) are saved as null. Loading a name that is not registered throws <see cref="InvalidDataException"/>.
/// The registry's table is <see cref="ComponentSerializerRegistry.Handles"/>; the ECS serialization registers it as a
/// service too.
/// </remarks>
public sealed class RenderHandleNames
{
	private readonly Table _meshes = new("mesh");
	private readonly Table _materials = new("material");
	private readonly Table _textures = new("texture");
	private readonly Table _targets = new("render target");

	/// <summary>Names a mesh (replacing an earlier name of the handle or of the name).</summary>
	public RenderHandleNames Name(MeshHandle mesh, string name) => Add(_meshes, mesh.Id, name);

	/// <summary>Names a material.</summary>
	public RenderHandleNames Name(MaterialHandle material, string name) => Add(_materials, material.Id, name);

	/// <summary>Names a texture.</summary>
	public RenderHandleNames Name(TextureHandle texture, string name) => Add(_textures, texture.Id, name);

	/// <summary>Names a render target.</summary>
	public RenderHandleNames Name(RenderTargetHandle target, string name) => Add(_targets, target.Id, name);

	/// <summary>
	/// Names every mesh, material and texture of <paramref name="model"/> after the model's name (its asset path):
	/// <c>{model}#mesh/{i}</c>, <c>{model}#material/{i}</c> and <c>{model}#texture/{i}</c>, the indices of its lists.
	/// Loading the same model again (or the asset manager's cached instance) gives the same names.
	/// </summary>
	public RenderHandleNames AddModel(IModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		for (var i = 0; i < model.Meshes.Count; i++) Name(model.Meshes[i], $"{model.Name}#mesh/{i}");
		for (var i = 0; i < model.Materials.Count; i++) Name(model.Materials[i], $"{model.Name}#material/{i}");
		for (var i = 0; i < model.Textures.Count; i++) Name(model.Textures[i], $"{model.Name}#texture/{i}");
		return this;
	}

	/// <summary>The saved form of a mesh handle: its name, <c>#id</c>, or null for none.</summary>
	public string? NameOf(MeshHandle mesh) => _meshes.NameOf(mesh.Id);

	/// <summary>The saved form of a material handle.</summary>
	public string? NameOf(MaterialHandle material) => _materials.NameOf(material.Id);

	/// <summary>The saved form of a texture handle.</summary>
	public string? NameOf(TextureHandle texture) => _textures.NameOf(texture.Id);

	/// <summary>The saved form of a render target handle.</summary>
	public string? NameOf(RenderTargetHandle target) => _targets.NameOf(target.Id);

	/// <summary>The mesh a saved name (or <c>#id</c>, or null) stands for.</summary>
	/// <exception cref="InvalidDataException">The name is not registered.</exception>
	public MeshHandle Mesh(string? name) => new(_meshes.IdOf(name));

	/// <summary>The material a saved name stands for.</summary>
	public MaterialHandle Material(string? name) => new(_materials.IdOf(name));

	/// <summary>The texture a saved name stands for.</summary>
	public TextureHandle Texture(string? name) => new(_textures.IdOf(name));

	/// <summary>The render target a saved name stands for.</summary>
	public RenderTargetHandle Target(string? name) => new(_targets.IdOf(name));

	private RenderHandleNames Add(Table table, int id, string name)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		if (name.StartsWith('#')) throw new ArgumentException($"A {table.Kind} name cannot start with '#' (saved run-time ids use it).", nameof(name));
		if (id <= 0) throw new ArgumentException($"Only a valid {table.Kind} handle can be named.", nameof(name));
		table.Set(id, name);
		return this;
	}

	private sealed class Table(string kind)
	{
		private readonly Dictionary<int, string> _names = [];
		private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

		public string Kind => kind;

		public void Set(int id, string name)
		{
			lock (_names)
			{
				if (_names.Remove(id, out var previous)) _ids.Remove(previous);
				if (_ids.Remove(name, out var other)) _names.Remove(other);
				_names[id] = name;
				_ids[name] = id;
			}
		}

		public string? NameOf(int id)
		{
			if (id <= 0) return null;
			lock (_names) return _names.TryGetValue(id, out var name) ? name : "#" + id.ToString(CultureInfo.InvariantCulture);
		}

		public int IdOf(string? name)
		{
			if (string.IsNullOrEmpty(name)) return 0;
			if (name[0] == '#' && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var raw)) return raw;
			lock (_names)
			{
				return _ids.TryGetValue(name, out var id) ? id
					: throw new InvalidDataException($"The saved {kind} '{name}' has no handle: name it with RenderHandleNames.Name (or AddModel) before loading.");
			}
		}
	}
}

/// <summary>The serializers of the 3D components (registered by <see cref="ComponentSerializerRegistry.CreateDefault"/>).</summary>
internal static class Components3D
{
	public static void Register(ComponentSerializerRegistry registry, RenderHandleNames names)
	{
		var json = EcsJsonContext.Default;
		registry.AddUnmanaged("DirectionalLight", json.DirectionalLight);
		registry.AddUnmanaged("PointLight", json.PointLight);
		registry.AddUnmanaged("SpotLight", json.SpotLight);

		var options = new JsonSerializerOptions { TypeInfoResolver = json };
		registry.Add("MeshRenderer", JsonMetadataServices.CreateValueInfo<MeshRenderer>(options, new MeshRendererConverter(names)),
			(BinaryWriter writer, in MeshRenderer value, EntityWriteMap _) =>
			{
				WriteName(writer, names.NameOf(value.Mesh));
				WriteName(writer, names.NameOf(value.Material));
				writer.Write(value.CastShadows);
				writer.Write(value.ReceiveShadows);
				writer.Write(value.LayerMask);
			},
			(reader, _) => new MeshRenderer(names.Mesh(ReadName(reader)), names.Material(ReadName(reader)))
			{
				CastShadows = reader.ReadBoolean(),
				ReceiveShadows = reader.ReadBoolean(),
				LayerMask = reader.ReadUInt32(),
			});

		registry.Add("Camera", JsonMetadataServices.CreateValueInfo<Camera>(options, new CameraConverter(names)),
			(BinaryWriter writer, in Camera value, EntityWriteMap _) =>
			{
				writer.Write((int)value.Projection);
				writer.Write(value.FieldOfView);
				writer.Write(value.OrthographicSize);
				writer.Write(value.Near);
				writer.Write(value.Far);
				Write(writer, value.Viewport);
				Write(writer, value.ClearColor);
				writer.Write((int)value.Clear);
				writer.Write(value.Priority);
				WriteName(writer, names.NameOf(value.Target));
				writer.Write(value.CullingMask);
			},
			(reader, _) => new Camera
			{
				Projection = (ProjectionKind)reader.ReadInt32(),
				FieldOfView = reader.ReadSingle(),
				OrthographicSize = reader.ReadSingle(),
				Near = reader.ReadSingle(),
				Far = reader.ReadSingle(),
				Viewport = ReadRectangle(reader),
				ClearColor = ReadColor(reader),
				Clear = (CameraClear)reader.ReadInt32(),
				Priority = reader.ReadInt32(),
				Target = names.Target(ReadName(reader)),
				CullingMask = reader.ReadUInt32(),
			});

		registry.Add("SceneEnvironment", JsonMetadataServices.CreateValueInfo<SceneEnvironment>(options, new EnvironmentConverter(names)),
			(BinaryWriter writer, in SceneEnvironment value, EntityWriteMap _) =>
			{
				Write(writer, value.AmbientColor);
				writer.Write(value.AmbientIntensity);
				WriteName(writer, names.NameOf(value.Skybox));
				writer.Write(value.SkyboxIntensity);
			},
			(reader, _) => new SceneEnvironment
			{
				AmbientColor = ReadColor(reader),
				AmbientIntensity = reader.ReadSingle(),
				Skybox = names.Texture(ReadName(reader)),
				SkyboxIntensity = reader.ReadSingle(),
			});
	}

	private static void WriteName(BinaryWriter writer, string? name)
	{
		writer.Write(name is not null);
		if (name is not null) writer.Write(name);
	}

	private static string? ReadName(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;

	private static void Write(BinaryWriter writer, Color color)
	{
		writer.Write(color.R);
		writer.Write(color.G);
		writer.Write(color.B);
		writer.Write(color.A);
	}

	private static Color ReadColor(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

	private static void Write(BinaryWriter writer, RectangleF rectangle)
	{
		writer.Write(rectangle.X);
		writer.Write(rectangle.Y);
		writer.Write(rectangle.Width);
		writer.Write(rectangle.Height);
	}

	private static RectangleF ReadRectangle(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

	/// <summary>Reads one property of a JSON object (the reader is on its value).</summary>
	private delegate void PropertyReader(string name, ref Utf8JsonReader reader);

	/// <summary>Reads the properties of a JSON object, calling <paramref name="property"/> on each value.</summary>
	private static void ReadObject(ref Utf8JsonReader reader, string type, PropertyReader property)
	{
		if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException($"{type} is an object.");
		while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
		{
			var name = reader.GetString()!;
			reader.Read();
			property(name, ref reader);
		}
	}

	private static string? String(ref Utf8JsonReader reader) => reader.TokenType == JsonTokenType.Null ? null : reader.GetString();

	private static void WriteName(Utf8JsonWriter writer, string property, string? name)
	{
		if (name is null) writer.WriteNull(property);
		else writer.WriteString(property, name);
	}

	private static Color ReadColor(ref Utf8JsonReader reader)
	{
		Span<float> v = stackalloc float[4];
		JsonFloats.Read(ref reader, v);
		return new Color(v[0], v[1], v[2], v[3]);
	}

	private static void WriteColor(Utf8JsonWriter writer, string property, Color color)
	{
		writer.WritePropertyName(property);
		JsonFloats.Write(writer, [color.R, color.G, color.B, color.A]);
	}

	private sealed class MeshRendererConverter(RenderHandleNames names) : JsonConverter<MeshRenderer>
	{
		public override MeshRenderer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			var result = new MeshRenderer();
			ReadObject(ref reader, "MeshRenderer", (string name, ref Utf8JsonReader r) =>
			{
				switch (name)
				{
					case "Mesh": result.Mesh = names.Mesh(String(ref r)); break;
					case "Material": result.Material = names.Material(String(ref r)); break;
					case "CastShadows": result.CastShadows = r.GetBoolean(); break;
					case "ReceiveShadows": result.ReceiveShadows = r.GetBoolean(); break;
					case "LayerMask": result.LayerMask = r.GetUInt32(); break;
					default: r.Skip(); break;
				}
			});
			return result;
		}

		public override void Write(Utf8JsonWriter writer, MeshRenderer value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			WriteName(writer, "Mesh", names.NameOf(value.Mesh));
			WriteName(writer, "Material", names.NameOf(value.Material));
			writer.WriteBoolean("CastShadows", value.CastShadows);
			writer.WriteBoolean("ReceiveShadows", value.ReceiveShadows);
			writer.WriteNumber("LayerMask", value.LayerMask);
			writer.WriteEndObject();
		}
	}

	private sealed class CameraConverter(RenderHandleNames names) : JsonConverter<Camera>
	{
		public override Camera Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			var result = new Camera();
			ReadObject(ref reader, "Camera", (string name, ref Utf8JsonReader r) =>
			{
				Span<float> v = stackalloc float[4];
				switch (name)
				{
					case "Projection": result.Projection = Enum.Parse<ProjectionKind>(r.GetString()!); break;
					case "FieldOfView": result.FieldOfView = r.GetSingle(); break;
					case "OrthographicSize": result.OrthographicSize = r.GetSingle(); break;
					case "Near": result.Near = r.GetSingle(); break;
					case "Far": result.Far = r.GetSingle(); break;
					case "Viewport": JsonFloats.Read(ref r, v); result.Viewport = new RectangleF(v[0], v[1], v[2], v[3]); break;
					case "ClearColor": result.ClearColor = ReadColor(ref r); break;
					case "Clear": result.Clear = Enum.Parse<CameraClear>(r.GetString()!); break;
					case "Priority": result.Priority = r.GetInt32(); break;
					case "Target": result.Target = names.Target(String(ref r)); break;
					case "CullingMask": result.CullingMask = r.GetUInt32(); break;
					default: r.Skip(); break;
				}
			});
			return result;
		}

		public override void Write(Utf8JsonWriter writer, Camera value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			writer.WriteString("Projection", value.Projection.ToString());
			writer.WriteNumber("FieldOfView", value.FieldOfView);
			writer.WriteNumber("OrthographicSize", value.OrthographicSize);
			writer.WriteNumber("Near", value.Near);
			writer.WriteNumber("Far", value.Far);
			writer.WritePropertyName("Viewport");
			JsonFloats.Write(writer, [value.Viewport.X, value.Viewport.Y, value.Viewport.Width, value.Viewport.Height]);
			WriteColor(writer, "ClearColor", value.ClearColor);
			writer.WriteString("Clear", value.Clear.ToString());
			writer.WriteNumber("Priority", value.Priority);
			WriteName(writer, "Target", names.NameOf(value.Target));
			writer.WriteNumber("CullingMask", value.CullingMask);
			writer.WriteEndObject();
		}
	}

	private sealed class EnvironmentConverter(RenderHandleNames names) : JsonConverter<SceneEnvironment>
	{
		public override SceneEnvironment Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			var result = new SceneEnvironment();
			ReadObject(ref reader, "SceneEnvironment", (string name, ref Utf8JsonReader r) =>
			{
				switch (name)
				{
					case "AmbientColor": result.AmbientColor = ReadColor(ref r); break;
					case "AmbientIntensity": result.AmbientIntensity = r.GetSingle(); break;
					case "Skybox": result.Skybox = names.Texture(String(ref r)); break;
					case "SkyboxIntensity": result.SkyboxIntensity = r.GetSingle(); break;
					default: r.Skip(); break;
				}
			});
			return result;
		}

		public override void Write(Utf8JsonWriter writer, SceneEnvironment value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			WriteColor(writer, "AmbientColor", value.AmbientColor);
			writer.WriteNumber("AmbientIntensity", value.AmbientIntensity);
			WriteName(writer, "Skybox", names.NameOf(value.Skybox));
			writer.WriteNumber("SkyboxIntensity", value.SkyboxIntensity);
			writer.WriteEndObject();
		}
	}
}

/// <summary>Writes a <see cref="Color"/> as its four channels, <c>[r, g, b, a]</c> in [0, 1].</summary>
internal sealed class ColorJsonConverter : JsonConverter<Color>
{
	public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		Span<float> v = stackalloc float[4];
		JsonFloats.Read(ref reader, v);
		return new Color(v[0], v[1], v[2], v[3]);
	}

	public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) => JsonFloats.Write(writer, [value.R, value.G, value.B, value.A]);
}
