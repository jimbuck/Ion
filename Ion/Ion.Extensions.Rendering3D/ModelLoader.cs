using System.Numerics;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering2D;
using Ion.Extensions.Rendering3D.Gltf;

namespace Ion.Extensions.Rendering3D;

/// <summary>
/// Loads glTF 2.0 models (<c>.gltf</c> with external or embedded buffers, and <c>.glb</c>) as <see cref="IModel"/>: every
/// triangle primitive becomes a mesh, every material a <see cref="PbrMaterial"/> (or an <see cref="UnlitMaterial"/> with
/// <c>KHR_materials_unlit</c>), every image a texture (decoded with ImageSharp like the 2D loader, straight alpha, full
/// mip chain, through the 2D renderer's <see cref="TextureFactory"/> so the sprite batch can draw it too), and the node
/// hierarchy of the default scene is kept. Without a GPU the geometry and materials are loaded and textures are
/// placeholders.
/// </summary>
internal sealed class ModelLoader(Renderer3D renderer, IPersistentStorage storage, TextureFactory? textures) : IAssetLoader<IModel>
{
	public Type AssetType { get; } = typeof(IModel);

	public IModel Load(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		var directory = Path.GetDirectoryName(path.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
		byte[] Resolve(string relative)
		{
			var file = directory.Length == 0 ? relative : directory + "/" + relative;
			using var stream = storage.Assets.Read(file);
			return ReadAll(stream);
		}

		byte[] bytes;
		using (var stream = storage.Assets.Read(path)) bytes = ReadAll(stream);
		var document = GltfReader.Read(bytes, Resolve);
		return Create(renderer, textures, path, document);
	}

	/// <summary>Creates the renderer resources of a parsed document.</summary>
	internal static Model Create(Renderer3D renderer, TextureFactory? factory, string name, GltfDocument document)
	{
		var owned = new List<ITexture2D>();
		var textureHandles = new List<TextureHandle>();
		var images = new TextureHandle[document.Images.Count];
		for (var i = 0; i < document.Images.Count; i++)
		{
			var image = document.Images[i];
			if (factory is null || !renderer.HasDevice)
			{
				images[i] = renderer.CreateTexturePlaceholder();
			}
			else
			{
				using var decoded = Image.Load<Rgba32>(image.Data);
				var pixels = new byte[decoded.Width * decoded.Height * 4];
				decoded.CopyPixelDataTo(pixels);
				var texture = factory.Create($"{name}#{image.Name}", (uint)decoded.Width, (uint)decoded.Height, pixels, premultiply: false);
				owned.Add(texture);
				images[i] = renderer.CreateTexture(texture);
			}

			textureHandles.Add(images[i]);
		}

		TextureHandle TextureOf(int texture) =>
			texture >= 0 && texture < document.Textures.Count && document.Textures[texture] is var image && image >= 0 && image < images.Length ? images[image] : default;

		var materials = new List<MaterialHandle>();
		foreach (var m in document.Materials)
		{
			var baseColor = ColorSpace.FromLinear(m.BaseColorFactor);
			if (m.Unlit)
			{
				materials.Add(renderer.CreateMaterial(new UnlitMaterial(baseColor, TextureOf(m.BaseColorTexture))
				{
					AlphaMode = m.AlphaMode,
					AlphaCutoff = m.AlphaCutoff,
					DoubleSided = m.DoubleSided,
				}));
				continue;
			}

			// glTF's emissive factor is linear and may exceed 1 (with KHR_materials_emissive_strength): keep the hue as an
			// sRGB color and the magnitude in the intensity.
			var emissive = m.EmissiveFactor * m.EmissiveStrength;
			var peak = MathF.Max(emissive.X, MathF.Max(emissive.Y, emissive.Z));
			var intensity = peak > 1 ? peak : 1f;
			materials.Add(renderer.CreateMaterial(new PbrMaterial
			{
				BaseColor = baseColor,
				BaseColorTexture = TextureOf(m.BaseColorTexture),
				Metallic = m.MetallicFactor,
				Roughness = m.RoughnessFactor,
				MetallicRoughnessTexture = TextureOf(m.MetallicRoughnessTexture),
				Normal = TextureOf(m.NormalTexture),
				NormalScale = m.NormalScale,
				Occlusion = TextureOf(m.OcclusionTexture),
				OcclusionStrength = m.OcclusionStrength,
				Emissive = ColorSpace.FromLinear(new Vector4(emissive / intensity, 1)),
				EmissiveIntensity = intensity,
				EmissiveTexture = TextureOf(m.EmissiveTexture),
				AlphaMode = m.AlphaMode,
				AlphaCutoff = m.AlphaCutoff,
				DoubleSided = m.DoubleSided,
			}));
		}

		// A glTF primitive without a material gets glTF's default material: white, metallic 1, roughness 1.
		MaterialHandle? defaultMaterial = null;
		MaterialHandle MaterialOf(int index)
		{
			if (index >= 0 && index < materials.Count) return materials[index];
			defaultMaterial ??= renderer.CreateMaterial(new PbrMaterial(Color.White, metallic: 1f, roughness: 1f));
			return defaultMaterial.Value;
		}

		var meshes = new List<MeshHandle>();
		var meshPrimitives = new List<ModelPrimitive[]>();
		foreach (var mesh in document.Meshes)
		{
			var primitives = new ModelPrimitive[mesh.Count];
			for (var p = 0; p < mesh.Count; p++)
			{
				var handle = renderer.CreateMesh(mesh[p].Mesh);
				meshes.Add(handle);
				primitives[p] = new ModelPrimitive(handle, MaterialOf(mesh[p].Material));
			}

			meshPrimitives.Add(primitives);
		}

		if (defaultMaterial is { } fallback) materials.Add(fallback);

		// Nodes: only those reachable from the default scene, with parents resolved.
		var parents = new int[document.Nodes.Count];
		Array.Fill(parents, -1);
		for (var i = 0; i < document.Nodes.Count; i++)
		{
			foreach (var child in document.Nodes[i].Children)
			{
				if (child < 0 || child >= parents.Length) throw new InvalidDataException($"Node {i} has an invalid child {child}.");
				if (parents[child] >= 0) throw new InvalidDataException($"Node {child} has two parents.");
				parents[child] = i;
			}
		}

		var reachable = new bool[document.Nodes.Count];
		var stack = new Stack<int>(document.SceneRoots);
		while (stack.Count > 0)
		{
			var n = stack.Pop();
			if (n < 0 || n >= reachable.Length || reachable[n]) continue;
			reachable[n] = true;
			foreach (var child in document.Nodes[n].Children) stack.Push(child);
		}

		var remap = new int[document.Nodes.Count];
		var nodes = new List<ModelNode>();
		var next = 0;
		for (var i = 0; i < document.Nodes.Count; i++) remap[i] = reachable[i] ? next++ : -1;
		for (var i = 0; i < document.Nodes.Count; i++)
		{
			if (!reachable[i]) continue;
			var node = document.Nodes[i];
			var primitives = node.Mesh >= 0 && node.Mesh < meshPrimitives.Count ? meshPrimitives[node.Mesh] : [];
			var parent = parents[i] >= 0 && reachable[parents[i]] ? remap[parents[i]] : -1;
			nodes.Add(new ModelNode(node.Name, parent, node.Transform, primitives));
		}

		ModelNode.Link(nodes);
		var roots = new List<int>();
		for (var i = 0; i < nodes.Count; i++) if (nodes[i].Parent < 0) roots.Add(i);

		var bounds = Aabb.Empty;
		foreach (var node in nodes)
		{
			foreach (var primitive in node.Primitives)
			{
				bounds.Encapsulate(renderer.GetMeshInfo(primitive.Mesh).Bounds.Transform(node.ModelMatrix));
			}
		}

		return new Model(renderer, name, nodes, roots, meshes, materials, textureHandles, owned, bounds);
	}

	private static byte[] ReadAll(Stream stream)
	{
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}

/// <summary>A loaded model; releases its meshes, materials and textures when disposed.</summary>
internal sealed class Model(Renderer3D renderer, string name, List<ModelNode> nodes, List<int> roots, List<MeshHandle> meshes, List<MaterialHandle> materials, List<TextureHandle> textures, List<ITexture2D> owned, Aabb bounds) : IModel
{
	private static long _nextId;
	private bool _disposed;

	public nint Id { get; } = (nint)Interlocked.Increment(ref _nextId);

	public string Name { get; } = name;

	public IReadOnlyList<ModelNode> Nodes { get; } = nodes;

	public IReadOnlyList<int> RootNodes { get; } = roots;

	public IReadOnlyList<MeshHandle> Meshes { get; } = meshes;

	public IReadOnlyList<MaterialHandle> Materials { get; } = materials;

	public IReadOnlyList<TextureHandle> Textures { get; } = textures;

	public Aabb Bounds { get; } = bounds;

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		foreach (var mesh in meshes) renderer.DestroyMesh(mesh);
		foreach (var material in materials) renderer.DestroyMaterial(material);
		foreach (var texture in textures) renderer.DestroyTexture(texture);
		foreach (var texture in owned) texture.Dispose();
	}
}
