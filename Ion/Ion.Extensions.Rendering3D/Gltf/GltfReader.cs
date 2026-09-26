using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Rendering3D.Gltf;

/// <summary>A glTF material, as parsed (factors in glTF's linear space; texture indices into <see cref="GltfDocument.Textures"/>, -1 for none).</summary>
internal sealed class GltfMaterial
{
	public string Name = "";
	public Vector4 BaseColorFactor = Vector4.One;
	public int BaseColorTexture = -1;
	public float MetallicFactor = 1f;
	public float RoughnessFactor = 1f;
	public int MetallicRoughnessTexture = -1;
	public int NormalTexture = -1;
	public float NormalScale = 1f;
	public int OcclusionTexture = -1;
	public float OcclusionStrength = 1f;
	public int EmissiveTexture = -1;
	public Vector3 EmissiveFactor;
	public float EmissiveStrength = 1f;
	public AlphaMode AlphaMode = AlphaMode.Opaque;
	public float AlphaCutoff = 0.5f;
	public bool DoubleSided;
	public bool Unlit;
}

/// <summary>A glTF mesh primitive: its geometry and material index (-1: the default material).</summary>
internal sealed record GltfPrimitive(MeshData Mesh, int Material);

/// <summary>A glTF node.</summary>
internal sealed record GltfNode(string Name, Transform Transform, int Mesh, int[] Children);

/// <summary>An image: its encoded bytes (PNG or JPEG) and name.</summary>
internal sealed record GltfImage(string Name, byte[] Data);

/// <summary>
/// A parsed glTF 2.0 asset: the subset Ion renders (meshes with triangle primitives, metallic-roughness materials,
/// textures and images, the node hierarchy and the default scene). Skins, animations, cameras, morph targets and sparse
/// accessors are not supported yet; extensions other than <c>KHR_materials_unlit</c> and
/// <c>KHR_materials_emissive_strength</c> are ignored.
/// </summary>
internal sealed class GltfDocument
{
	public List<List<GltfPrimitive>> Meshes { get; } = [];
	public List<GltfMaterial> Materials { get; } = [];

	/// <summary>Texture index to image index (-1 when the texture has no supported image).</summary>
	public List<int> Textures { get; } = [];

	public List<GltfImage> Images { get; } = [];
	public List<GltfNode> Nodes { get; } = [];
	public List<int> SceneRoots { get; } = [];
}

/// <summary>
/// A minimal, allocation-conscious glTF 2.0 reader over <see cref="JsonDocument"/> (reflection-free, NativeAOT clean):
/// <c>.gltf</c> with external or data URI buffers and images, and binary <c>.glb</c>.
/// </summary>
internal static class GltfReader
{
	private const uint GlbMagic = 0x46546C67; // "glTF"
	private const uint ChunkJson = 0x4E4F534A;
	private const uint ChunkBin = 0x004E4942;

	/// <summary>
	/// Parses a glTF asset. <paramref name="resolve"/> reads a file referenced by a relative URI (buffers, images).
	/// </summary>
	/// <exception cref="InvalidDataException">The data is not valid glTF 2.0, or uses an unsupported feature.</exception>
	public static GltfDocument Read(ReadOnlySpan<byte> data, Func<string, byte[]> resolve)
	{
		byte[] json;
		byte[]? glbBin = null;
		if (data.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(data) == GlbMagic)
		{
			(json, glbBin) = SplitGlb(data);
		}
		else
		{
			json = data.ToArray();
		}

		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;
		if (root.TryGetProperty("asset", out var asset) && asset.TryGetProperty("version", out var version) && !(version.GetString() ?? "").StartsWith('2'))
		{
			throw new InvalidDataException($"glTF version {version.GetString()} is not supported (2.0 only).");
		}

		if (root.TryGetProperty("extensionsRequired", out var required))
		{
			foreach (var extension in required.EnumerateArray())
			{
				var name = extension.GetString();
				if (name is not ("KHR_materials_unlit" or "KHR_materials_emissive_strength")) throw new InvalidDataException($"glTF extension {name} is required but not supported.");
			}
		}

		var result = new GltfDocument();
		var buffers = ReadBuffers(root, glbBin, resolve);
		var views = ReadBufferViews(root, buffers);
		var accessors = Array(root, "accessors");

		// Images and textures.
		foreach (var image in Array(root, "images"))
		{
			var name = String(image, "name") ?? String(image, "uri") ?? $"image{result.Images.Count}";
			byte[] bytes;
			if (String(image, "uri") is { } uri) bytes = LoadUri(uri, resolve);
			else if (image.TryGetProperty("bufferView", out var viewIndex)) bytes = views[viewIndex.GetInt32()].ToArray();
			else throw new InvalidDataException($"Image {result.Images.Count} has neither a uri nor a bufferView.");
			result.Images.Add(new GltfImage(name, bytes));
		}

		foreach (var texture in Array(root, "textures"))
		{
			result.Textures.Add(texture.TryGetProperty("source", out var source) ? source.GetInt32() : -1);
		}

		// Materials.
		foreach (var material in Array(root, "materials"))
		{
			var m = new GltfMaterial { Name = String(material, "name") ?? $"material{result.Materials.Count}" };
			if (material.TryGetProperty("pbrMetallicRoughness", out var pbr))
			{
				if (pbr.TryGetProperty("baseColorFactor", out var factor)) m.BaseColorFactor = Vector4Of(factor);
				m.BaseColorTexture = TextureIndex(pbr, "baseColorTexture");
				m.MetallicFactor = Float(pbr, "metallicFactor", 1f);
				m.RoughnessFactor = Float(pbr, "roughnessFactor", 1f);
				m.MetallicRoughnessTexture = TextureIndex(pbr, "metallicRoughnessTexture");
			}

			m.NormalTexture = TextureIndex(material, "normalTexture");
			if (material.TryGetProperty("normalTexture", out var normal)) m.NormalScale = Float(normal, "scale", 1f);
			m.OcclusionTexture = TextureIndex(material, "occlusionTexture");
			if (material.TryGetProperty("occlusionTexture", out var occlusion)) m.OcclusionStrength = Float(occlusion, "strength", 1f);
			m.EmissiveTexture = TextureIndex(material, "emissiveTexture");
			if (material.TryGetProperty("emissiveFactor", out var emissive))
			{
				var e = Vector4Of(emissive);
				m.EmissiveFactor = new Vector3(e.X, e.Y, e.Z);
			}

			m.AlphaMode = String(material, "alphaMode") switch
			{
				"MASK" => AlphaMode.Mask,
				"BLEND" => AlphaMode.Blend,
				_ => AlphaMode.Opaque,
			};
			m.AlphaCutoff = Float(material, "alphaCutoff", 0.5f);
			m.DoubleSided = material.TryGetProperty("doubleSided", out var doubleSided) && doubleSided.GetBoolean();
			if (material.TryGetProperty("extensions", out var extensions))
			{
				m.Unlit = extensions.TryGetProperty("KHR_materials_unlit", out _);
				if (extensions.TryGetProperty("KHR_materials_emissive_strength", out var strength)) m.EmissiveStrength = Float(strength, "emissiveStrength", 1f);
			}

			result.Materials.Add(m);
		}

		// Meshes.
		foreach (var mesh in Array(root, "meshes"))
		{
			var meshName = String(mesh, "name") ?? $"mesh{result.Meshes.Count}";
			var primitives = new List<GltfPrimitive>();
			var index = 0;
			foreach (var primitive in Array(mesh, "primitives"))
			{
				var mode = primitive.TryGetProperty("mode", out var modeElement) ? modeElement.GetInt32() : 4;
				if (mode != 4) continue; // Only triangle lists; points, lines and strips are skipped.
				if (!primitive.TryGetProperty("attributes", out var attributes) || !attributes.TryGetProperty("POSITION", out var positionAccessor))
				{
					throw new InvalidDataException($"Mesh '{meshName}' primitive {index} has no POSITION.");
				}

				var positions = ReadVec3(accessors[positionAccessor.GetInt32()], views);
				uint[] indices;
				if (primitive.TryGetProperty("indices", out var indicesAccessor)) indices = ReadIndices(accessors[indicesAccessor.GetInt32()], views);
				else
				{
					indices = new uint[positions.Length];
					for (var i = 0; i < indices.Length; i++) indices[i] = (uint)i;
				}

				var meshData = new MeshData(primitives.Count == 0 && Array(mesh, "primitives").Count == 1 ? meshName : $"{meshName}.{index}", positions, indices);
				if (attributes.TryGetProperty("NORMAL", out var normalAccessor)) meshData.Normals = ReadVec3(accessors[normalAccessor.GetInt32()], views);
				if (attributes.TryGetProperty("TANGENT", out var tangentAccessor)) meshData.Tangents = ReadVec4(accessors[tangentAccessor.GetInt32()], views);
				if (attributes.TryGetProperty("TEXCOORD_0", out var uv0Accessor)) meshData.Uv0 = ReadVec2(accessors[uv0Accessor.GetInt32()], views);
				if (attributes.TryGetProperty("TEXCOORD_1", out var uv1Accessor)) meshData.Uv1 = ReadVec2(accessors[uv1Accessor.GetInt32()], views);
				if (attributes.TryGetProperty("COLOR_0", out var colorAccessor))
				{
					var colors = ReadVec4(accessors[colorAccessor.GetInt32()], views, fillW: 1f);
					// glTF vertex colors are linear; MeshData colors are sRGB.
					meshData.Colors = new Color[colors.Length];
					for (var i = 0; i < colors.Length; i++) meshData.Colors[i] = ColorSpace.FromLinear(colors[i]);
				}

				var materialIndex = primitive.TryGetProperty("material", out var materialElement) ? materialElement.GetInt32() : -1;
				primitives.Add(new GltfPrimitive(meshData, materialIndex));
				index++;
			}

			result.Meshes.Add(primitives);
		}

		// Nodes and the default scene.
		foreach (var node in Array(root, "nodes"))
		{
			var transform = new Transform();
			if (node.TryGetProperty("matrix", out var matrix))
			{
				var m = new float[16];
				var i = 0;
				foreach (var value in matrix.EnumerateArray()) m[i++] = value.GetSingle();
				// Column-major column-vector storage read row by row is the row-vector matrix System.Numerics uses.
				transform = Transform.FromMatrix(new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]));
			}
			else
			{
				if (node.TryGetProperty("translation", out var translation)) transform.Position = Vector3Of(translation);
				if (node.TryGetProperty("rotation", out var rotation))
				{
					var q = Vector4Of(rotation);
					transform.Rotation = Quaternion.Normalize(new Quaternion(q.X, q.Y, q.Z, q.W));
				}

				if (node.TryGetProperty("scale", out var scale)) transform.Scale = Vector3Of(scale);
			}

			var children = new List<int>();
			foreach (var child in Array(node, "children")) children.Add(child.GetInt32());
			result.Nodes.Add(new GltfNode(String(node, "name") ?? $"node{result.Nodes.Count}", transform, node.TryGetProperty("mesh", out var meshIndex) ? meshIndex.GetInt32() : -1, [.. children]));
		}

		var scenes = Array(root, "scenes");
		var sceneIndex = root.TryGetProperty("scene", out var scene) ? scene.GetInt32() : 0;
		if (scenes.Count > 0 && sceneIndex < scenes.Count)
		{
			foreach (var node in Array(scenes[sceneIndex], "nodes")) result.SceneRoots.Add(node.GetInt32());
		}
		else
		{
			// No scene: every node without a parent is a root.
			var hasParent = new bool[result.Nodes.Count];
			foreach (var node in result.Nodes) foreach (var child in node.Children) hasParent[child] = true;
			for (var i = 0; i < hasParent.Length; i++) if (!hasParent[i]) result.SceneRoots.Add(i);
		}

		return result;
	}

	private static (byte[] Json, byte[]? Bin) SplitGlb(ReadOnlySpan<byte> data)
	{
		var version = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
		if (version != 2) throw new InvalidDataException($"GLB version {version} is not supported (2 only).");
		var length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[8..]), (uint)data.Length);
		var offset = 12;
		byte[]? json = null;
		byte[]? bin = null;
		while (offset + 8 <= length)
		{
			var chunkLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
			var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
			var chunk = data.Slice(offset + 8, Math.Min(chunkLength, length - offset - 8));
			if (chunkType == ChunkJson) json = chunk.ToArray();
			else if (chunkType == ChunkBin && bin is null) bin = chunk.ToArray();
			offset += 8 + ((chunkLength + 3) & ~3);
		}

		if (json is null) throw new InvalidDataException("The GLB has no JSON chunk.");
		return (json, bin);
	}

	private static List<byte[]> ReadBuffers(JsonElement root, byte[]? glbBin, Func<string, byte[]> resolve)
	{
		var buffers = new List<byte[]>();
		foreach (var buffer in Array(root, "buffers"))
		{
			if (String(buffer, "uri") is { } uri) buffers.Add(LoadUri(uri, resolve));
			else if (buffers.Count == 0 && glbBin is not null) buffers.Add(glbBin);
			else throw new InvalidDataException($"Buffer {buffers.Count} has no uri and is not the GLB binary chunk.");
		}

		return buffers;
	}

	private readonly record struct View(byte[] Buffer, int Offset, int Length, int Stride)
	{
		public ReadOnlySpan<byte> Span => Buffer.AsSpan(Offset, Length);

		public byte[] ToArray() => Span.ToArray();
	}

	private static List<View> ReadBufferViews(JsonElement root, List<byte[]> buffers)
	{
		var views = new List<View>();
		foreach (var view in Array(root, "bufferViews"))
		{
			var buffer = buffers[view.GetProperty("buffer").GetInt32()];
			var offset = Int(view, "byteOffset", 0);
			var length = view.GetProperty("byteLength").GetInt32();
			if (offset + length > buffer.Length) throw new InvalidDataException($"Buffer view {views.Count} overflows its buffer.");
			views.Add(new View(buffer, offset, length, Int(view, "byteStride", 0)));
		}

		return views;
	}

	private static byte[] LoadUri(string uri, Func<string, byte[]> resolve)
	{
		if (uri.StartsWith("data:", StringComparison.Ordinal))
		{
			var comma = uri.IndexOf(',');
			if (comma < 0 || !uri.AsSpan(0, comma).EndsWith(";base64", StringComparison.Ordinal)) throw new InvalidDataException("Only base64 data URIs are supported.");
			return Convert.FromBase64String(uri[(comma + 1)..]);
		}

		return resolve(Uri.UnescapeDataString(uri));
	}

	// Accessors.

	private static int ComponentCount(string type) => type switch
	{
		"SCALAR" => 1,
		"VEC2" => 2,
		"VEC3" => 3,
		"VEC4" => 4,
		"MAT2" => 4,
		"MAT3" => 9,
		"MAT4" => 16,
		_ => throw new InvalidDataException($"Unknown accessor type {type}."),
	};

	private static int ComponentSize(int componentType) => componentType switch
	{
		5120 or 5121 => 1,
		5122 or 5123 => 2,
		5125 or 5126 => 4,
		_ => throw new InvalidDataException($"Unknown accessor component type {componentType}."),
	};

	/// <summary>Reads an accessor as floats, <paramref name="components"/> per element (normalized integers mapped to [0, 1] or [-1, 1]).</summary>
	private static float[] ReadFloats(JsonElement accessor, List<View> views, int components, float fill = 0f)
	{
		if (accessor.TryGetProperty("sparse", out _)) throw new InvalidDataException("Sparse accessors are not supported.");
		var count = accessor.GetProperty("count").GetInt32();
		var type = accessor.GetProperty("componentType").GetInt32();
		var available = ComponentCount(accessor.GetProperty("type").GetString() ?? "");
		var normalized = accessor.TryGetProperty("normalized", out var n) && n.GetBoolean();
		var result = new float[count * components];
		if (fill != 0f) result.AsSpan().Fill(fill);
		if (!accessor.TryGetProperty("bufferView", out var viewIndex)) return result; // All zeros (the spec's default).

		var view = views[viewIndex.GetInt32()];
		var size = ComponentSize(type);
		var stride = view.Stride > 0 ? view.Stride : size * available;
		var offset = Int(accessor, "byteOffset", 0);
		var span = view.Span;
		if (offset + (long)stride * Math.Max(0, count - 1) + size * available > span.Length) throw new InvalidDataException("An accessor overflows its buffer view.");
		var take = Math.Min(available, components);
		for (var i = 0; i < count; i++)
		{
			var element = span[(offset + i * stride)..];
			for (var c = 0; c < take; c++)
			{
				var value = element[(c * size)..];
				result[i * components + c] = type switch
				{
					5126 => BinaryPrimitives.ReadSingleLittleEndian(value),
					5121 => normalized ? value[0] / 255f : value[0],
					5120 => normalized ? MathF.Max((sbyte)value[0] / 127f, -1f) : (sbyte)value[0],
					5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian(value) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian(value),
					5122 => normalized ? MathF.Max(BinaryPrimitives.ReadInt16LittleEndian(value) / 32767f, -1f) : BinaryPrimitives.ReadInt16LittleEndian(value),
					_ => BinaryPrimitives.ReadUInt32LittleEndian(value),
				};
			}
		}

		return result;
	}

	private static Vector2[] ReadVec2(JsonElement accessor, List<View> views)
	{
		var f = ReadFloats(accessor, views, 2);
		var result = new Vector2[f.Length / 2];
		for (var i = 0; i < result.Length; i++) result[i] = new Vector2(f[i * 2], f[i * 2 + 1]);
		return result;
	}

	private static Vector3[] ReadVec3(JsonElement accessor, List<View> views)
	{
		var f = ReadFloats(accessor, views, 3);
		var result = new Vector3[f.Length / 3];
		for (var i = 0; i < result.Length; i++) result[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
		return result;
	}

	private static Vector4[] ReadVec4(JsonElement accessor, List<View> views, float fillW = 0f)
	{
		var available = ComponentCount(accessor.GetProperty("type").GetString() ?? "");
		var f = ReadFloats(accessor, views, 4);
		var result = new Vector4[f.Length / 4];
		for (var i = 0; i < result.Length; i++) result[i] = new Vector4(f[i * 4], f[i * 4 + 1], f[i * 4 + 2], available < 4 ? fillW : f[i * 4 + 3]);
		return result;
	}

	private static uint[] ReadIndices(JsonElement accessor, List<View> views)
	{
		var type = accessor.GetProperty("componentType").GetInt32();
		if (type is not (5121 or 5123 or 5125)) throw new InvalidDataException($"Index component type {type} is not an unsigned integer.");
		var f = ReadFloats(accessor, views, 1);
		if (type == 5125)
		{
			// Floats cannot hold every uint: read 32-bit indices directly.
			var count = accessor.GetProperty("count").GetInt32();
			var view = views[accessor.GetProperty("bufferView").GetInt32()];
			var offset = Int(accessor, "byteOffset", 0);
			var stride = view.Stride > 0 ? view.Stride : 4;
			var result32 = new uint[count];
			for (var i = 0; i < count; i++) result32[i] = BinaryPrimitives.ReadUInt32LittleEndian(view.Span[(offset + i * stride)..]);
			return result32;
		}

		var result = new uint[f.Length];
		for (var i = 0; i < f.Length; i++) result[i] = (uint)f[i];
		return result;
	}

	// JSON helpers.

	private static List<JsonElement> Array(JsonElement element, string name)
	{
		var list = new List<JsonElement>();
		if (element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in array.EnumerateArray()) list.Add(item);
		}

		return list;
	}

	private static string? String(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static float Float(JsonElement element, string name, float fallback) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : fallback;

	private static int Int(JsonElement element, string name, int fallback) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : fallback;

	private static int TextureIndex(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var info) || !info.TryGetProperty("index", out var index)) return -1;
		return index.GetInt32();
	}

	private static Vector3 Vector3Of(JsonElement array)
	{
		Span<float> v = stackalloc float[3];
		var i = 0;
		foreach (var value in array.EnumerateArray())
		{
			if (i < 3) v[i] = value.GetSingle();
			i++;
		}

		return new Vector3(v[0], v[1], v[2]);
	}

	private static Vector4 Vector4Of(JsonElement array)
	{
		Span<float> v = [0, 0, 0, 1];
		var i = 0;
		foreach (var value in array.EnumerateArray())
		{
			if (i < 4) v[i] = value.GetSingle();
			i++;
		}

		return new Vector4(v[0], v[1], v[2], v[3]);
	}
}
