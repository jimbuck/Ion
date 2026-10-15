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
/// hierarchy of the default scene is kept. Every skin becomes a <see cref="Skeleton"/> (<see cref="IModel.Skins"/>) and
/// every animation an <see cref="AnimationClip"/> of the skin whose joints it animates (<see cref="IModel.Animations"/>).
/// Without a GPU the geometry and materials are loaded and textures are placeholders.
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
		var meshSkinned = new List<bool[]>();
		foreach (var mesh in document.Meshes)
		{
			var primitives = new ModelPrimitive[mesh.Count];
			var skinned = new bool[mesh.Count];
			for (var p = 0; p < mesh.Count; p++)
			{
				var handle = renderer.CreateMesh(mesh[p].Mesh);
				meshes.Add(handle);
				primitives[p] = new ModelPrimitive(handle, MaterialOf(mesh[p].Material));
				skinned[p] = mesh[p].Mesh.IsSkinned;
			}

			meshPrimitives.Add(primitives);
			meshSkinned.Add(skinned);
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
			if (node.Skin >= 0 && node.Skin < document.Skins.Count && primitives.Length > 0)
			{
				// The node's skinned primitives are moved by its skin (rigid ones stay at the node).
				var skinned = meshSkinned[node.Mesh];
				primitives = [.. primitives];
				for (var p = 0; p < primitives.Length; p++)
				{
					if (skinned[p]) primitives[p] = primitives[p] with { Skin = node.Skin };
				}
			}

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
				// A skinned mesh is posed in model space (its bind pose is where its vertices are).
				var info = renderer.GetMeshInfo(primitive.Mesh).Bounds;
				bounds.Encapsulate(primitive.Skin >= 0 ? info : info.Transform(node.ModelMatrix));
			}
		}

		var (skins, animations, skinOf) = CreateAnimation(document, parents);
		return new Model(renderer, name, nodes, roots, meshes, materials, textureHandles, owned, bounds)
		{
			Skins = skins,
			Animations = animations,
			SkinIndex = skinOf,
		};
	}

	/// <summary>
	/// The skeletons of the skins and the clips of the animations. A skin's joints keep its order (the vertices' joint
	/// indices); a joint's parent is its nearest ancestor among them, and the root joints hang under the model matrix of
	/// the first root joint's parent node. An animation belongs to the skin with the most of its channels' nodes; channels
	/// of other nodes are dropped, and an animation of no joint is skipped.
	/// </summary>
	private static (List<Skeleton> Skins, List<AnimationClip> Animations, Dictionary<AnimationClip, int> SkinOf) CreateAnimation(GltfDocument document, int[] parents)
	{
		var skins = new List<Skeleton>();
		var jointMaps = new List<Dictionary<int, int>>();
		var model = new Matrix4x4?[document.Nodes.Count];
		Matrix4x4 ModelMatrix(int node)
		{
			if (model[node] is { } known) return known;
			var local = document.Nodes[node].Transform.ToMatrix();
			var result = parents[node] >= 0 ? local * ModelMatrix(parents[node]) : local;
			model[node] = result;
			return result;
		}

		foreach (var skin in document.Skins)
		{
			var map = new Dictionary<int, int>();
			for (var j = 0; j < skin.Joints.Length; j++) map.TryAdd(skin.Joints[j], j);
			var joints = new SkeletonJoint[skin.Joints.Length];
			Matrix4x4? root = null;
			for (var j = 0; j < joints.Length; j++)
			{
				var node = skin.Joints[j];
				var parent = -1;
				for (var up = parents[node]; up >= 0; up = parents[up])
				{
					if (map.TryGetValue(up, out var p)) { parent = p; break; }
				}

				if (parent < 0 && root is null) root = parents[node] >= 0 ? ModelMatrix(parents[node]) : Matrix4x4.Identity;
				joints[j] = new SkeletonJoint(document.Nodes[node].Name, parent, document.Nodes[node].Transform, skin.InverseBind[j]);
			}

			skins.Add(new Skeleton(skin.Name, joints, root));
			jointMaps.Add(map);
		}

		var animations = new List<AnimationClip>();
		var skinOf = new Dictionary<AnimationClip, int>();
		foreach (var animation in document.Animations)
		{
			var best = -1;
			var bestCount = 0;
			for (var s = 0; s < jointMaps.Count; s++)
			{
				var count = 0;
				foreach (var channel in animation.Channels) if (jointMaps[s].ContainsKey(channel.Node)) count++;
				if (count > bestCount) (best, bestCount) = (s, count);
			}

			if (best < 0) continue;
			var channels = new List<AnimationChannel>();
			foreach (var channel in animation.Channels)
			{
				if (jointMaps[best].TryGetValue(channel.Node, out var joint)) channels.Add(new AnimationChannel(joint, channel.Path, channel.Interpolation, channel.Times, channel.Values));
			}

			var clip = new AnimationClip(animation.Name, channels);
			animations.Add(clip);
			skinOf[clip] = best;
		}

		return (skins, animations, skinOf);
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

	public IReadOnlyList<Skeleton> Skins { get; init; } = [];

	public IReadOnlyList<AnimationClip> Animations { get; init; } = [];

	internal Dictionary<AnimationClip, int> SkinIndex { get; init; } = [];

	public int SkinOf(AnimationClip clip) => SkinIndex.TryGetValue(clip, out var skin) ? skin : -1;

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
