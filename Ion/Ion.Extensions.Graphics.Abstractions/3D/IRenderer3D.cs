using System.Numerics;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics;

/// <summary>
/// The immediate-mode submission API of the 3D renderer: every frame, during the Render stage, add cameras, lights and
/// mesh renderers with their world matrices; the renderer culls, sorts, batches and draws them when the stage ends.
/// Nothing persists from one frame to the next. Games without ECS call it from their Render steps; the ECS extraction
/// systems call it for every entity with the matching components, so there is one renderer either way.
/// </summary>
/// <remarks>
/// <para>
/// Submissions are copied (the arguments are <c>in</c> parameters of unmanaged types): 10,000 mesh renderers cost a
/// copy each, and nothing is allocated per frame once the renderer's arrays have grown to the scene.
/// </para>
/// <para>
/// World matrices use the row-vector convention of <see cref="Transform.ToMatrix"/> (<c>child * parent</c>).
/// </para>
/// </remarks>
public interface IMeshBatch
{
	/// <summary>Adds a camera placed by <paramref name="world"/> (a camera looks down its -Z axis) for this frame. Cameras render by ascending <see cref="Camera.Priority"/>.</summary>
	void AddCamera(in Camera camera, in Matrix4x4 world);

	/// <summary>Replaces this frame's cameras with one camera placed by <paramref name="transform"/>.</summary>
	void SetCamera(in Camera camera, in Transform transform);

	/// <summary>Draws <paramref name="renderer"/>'s mesh with its material at <paramref name="world"/> in every camera whose culling mask includes its layers.</summary>
	void Submit(in MeshRenderer renderer, in Matrix4x4 world);

	/// <summary>Draws <paramref name="mesh"/> with <paramref name="material"/> at <paramref name="world"/> (casting and receiving shadows, layer 1).</summary>
	void Draw(MeshHandle mesh, MaterialHandle material, in Matrix4x4 world);

	/// <summary>Adds a directional light shining along the -Z axis of <paramref name="world"/>.</summary>
	void AddLight(in DirectionalLight light, in Matrix4x4 world);

	/// <summary>Adds a directional light shining along <paramref name="direction"/>.</summary>
	void AddLight(in DirectionalLight light, Vector3 direction);

	/// <summary>Adds a point light at <paramref name="position"/>.</summary>
	void AddLight(in PointLight light, Vector3 position);

	/// <summary>Adds a spot light at the origin of <paramref name="world"/> shining along its -Z axis.</summary>
	void AddLight(in SpotLight light, in Matrix4x4 world);

	/// <summary>Sets this frame's ambient light and skybox (kept until changed: the environment is the one piece of state that persists).</summary>
	void SetEnvironment(in SceneEnvironment environment);
}

/// <summary>
/// The 3D renderer: <see cref="IMeshBatch"/> submission plus the resources it draws (meshes, materials, textures,
/// render targets). Resources are created at load time (Init steps or later) and addressed by unmanaged handles.
/// </summary>
/// <remarks>
/// Without a GPU (the headless null backend) the renderer still accepts resources and submissions and runs its CPU
/// pipeline (culling, sorting, batching), so <see cref="LastFrameStatistics"/> is meaningful in tests; nothing is drawn.
/// </remarks>
public interface IRenderer3D : IMeshBatch
{
	/// <summary>Uploads a mesh (interleaved into <see cref="MeshVertex"/>es). Missing normals and tangents are computed.</summary>
	MeshHandle CreateMesh(MeshData data);

	/// <summary>What the renderer knows about a mesh.</summary>
	MeshInfo GetMeshInfo(MeshHandle mesh);

	/// <summary>Releases a mesh (its GPU buffers go once no frame in flight uses them).</summary>
	void DestroyMesh(MeshHandle mesh);

	/// <summary>Creates an unlit material.</summary>
	MaterialHandle CreateMaterial(in UnlitMaterial material);

	/// <summary>Creates a PBR material.</summary>
	MaterialHandle CreateMaterial(in PbrMaterial material);

	/// <summary>
	/// Creates a material of a custom shader with its uniform block bytes (std140, <see cref="MaterialShaderDescriptor.UniformSize"/>)
	/// and textures (bindings 1 and up).
	/// </summary>
	MaterialHandle CreateMaterial(MaterialShaderHandle shader, ReadOnlySpan<byte> uniforms, ReadOnlySpan<TextureHandle> textures, AlphaMode alphaMode = AlphaMode.Opaque, bool doubleSided = false);

	/// <summary>Changes an unlit material's parameters (textures included).</summary>
	void UpdateMaterial(MaterialHandle handle, in UnlitMaterial material);

	/// <summary>Changes a PBR material's parameters (textures included).</summary>
	void UpdateMaterial(MaterialHandle handle, in PbrMaterial material);

	/// <summary>Releases a material.</summary>
	void DestroyMaterial(MaterialHandle material);

	/// <summary>Registers a custom material shader (compiled at build time, see <see cref="MaterialShaderDescriptor"/>).</summary>
	MaterialShaderHandle CreateMaterialShader(MaterialShaderDescriptor descriptor);

	/// <summary>Registers a texture loaded by the 2D renderer (<c>Load&lt;ITexture2D&gt;</c>, a render target) for materials. The texture stays owned by its loader.</summary>
	TextureHandle CreateTexture(ITexture2D texture);

	/// <summary>Registers an RHI texture (2D or cube map); with <paramref name="ownsTexture"/> the renderer disposes it at teardown.</summary>
	TextureHandle CreateTexture(ITexture texture, bool ownsTexture = false);

	/// <summary>
	/// Creates a <paramref name="width"/> by <paramref name="height"/> color render target (with its own depth) that
	/// cameras render into (<see cref="Camera.Target"/>). <see cref="GetRenderTargetTexture"/> gives the color texture,
	/// which materials can sample and the sprite batch can draw.
	/// </summary>
	RenderTargetHandle CreateRenderTarget(uint width, uint height, string? name = null);

	/// <summary>The texture handle of a render target's color, for materials.</summary>
	TextureHandle GetRenderTargetTexture(RenderTargetHandle target);

	/// <summary>What the renderer did in the last frame.</summary>
	Rendering3DStatistics LastFrameStatistics { get; }
}

/// <summary>One mesh of a model node, drawn with its material.</summary>
/// <param name="Mesh">The mesh.</param>
/// <param name="Material">The material.</param>
public readonly record struct ModelPrimitive(MeshHandle Mesh, MaterialHandle Material);

/// <summary>A node of a model's hierarchy.</summary>
public sealed class ModelNode
{
	/// <summary>Creates a node.</summary>
	public ModelNode(string name, int parent, Transform localTransform, ModelPrimitive[] primitives)
	{
		Name = name;
		Parent = parent;
		LocalTransform = localTransform;
		Primitives = primitives;
	}

	/// <summary>The node's name.</summary>
	public string Name { get; }

	/// <summary>The index of the parent node, or -1 for a root.</summary>
	public int Parent { get; }

	/// <summary>The indices of the child nodes.</summary>
	public int[] Children { get; internal set; } = [];

	/// <summary>The transform relative to the parent.</summary>
	public Transform LocalTransform { get; }

	/// <summary>The matrix relative to the model's root (every ancestor's local transform applied).</summary>
	public Matrix4x4 ModelMatrix { get; internal set; } = Matrix4x4.Identity;

	/// <summary>The meshes drawn at this node (glTF primitives), possibly none.</summary>
	public ModelPrimitive[] Primitives { get; }

	/// <summary>Sets the children and model matrices of <paramref name="nodes"/> from their parents (parents may come after children).</summary>
	public static void Link(IReadOnlyList<ModelNode> nodes)
	{
		ArgumentNullException.ThrowIfNull(nodes);
		var children = new List<int>[nodes.Count];
		for (var i = 0; i < nodes.Count; i++) children[i] = [];
		for (var i = 0; i < nodes.Count; i++)
		{
			if (nodes[i].Parent >= 0) children[nodes[i].Parent].Add(i);
		}

		for (var i = 0; i < nodes.Count; i++) nodes[i].Children = [.. children[i]];

		var done = new bool[nodes.Count];
		for (var i = 0; i < nodes.Count; i++) Resolve(i);

		void Resolve(int index)
		{
			if (done[index]) return;
			var node = nodes[index];
			var local = node.LocalTransform.ToMatrix();
			if (node.Parent >= 0)
			{
				Resolve(node.Parent);
				node.ModelMatrix = local * nodes[node.Parent].ModelMatrix;
			}
			else
			{
				node.ModelMatrix = local;
			}

			done[index] = true;
		}
	}
}

/// <summary>
/// A 3D model: meshes, materials, textures and a node hierarchy, loaded from glTF 2.0 with
/// <c>Load&lt;IModel&gt;("model.gltf")</c> (or <c>.glb</c>). The model's GPU resources belong to the renderer that loaded
/// it and are released with the asset.
/// </summary>
public interface IModel : IAsset
{
	/// <summary>The nodes, parents before or after children (see <see cref="ModelNode.Parent"/>).</summary>
	IReadOnlyList<ModelNode> Nodes { get; }

	/// <summary>The indices of the root nodes of the default scene.</summary>
	IReadOnlyList<int> RootNodes { get; }

	/// <summary>Every mesh of the model.</summary>
	IReadOnlyList<MeshHandle> Meshes { get; }

	/// <summary>Every material of the model.</summary>
	IReadOnlyList<MaterialHandle> Materials { get; }

	/// <summary>Every texture of the model.</summary>
	IReadOnlyList<TextureHandle> Textures { get; }

	/// <summary>The bounds of the whole model in model space (every node's meshes at their model matrices).</summary>
	Aabb Bounds { get; }
}

/// <summary>A cube map texture loaded from six images (<c>Load&lt;ICubemap&gt;("skybox")</c>), for skyboxes and ambient light.</summary>
public interface ICubemap : IAsset
{
	/// <summary>The texture handle to put in <see cref="SceneEnvironment.Skybox"/>.</summary>
	TextureHandle Handle { get; }

	/// <summary>The width and height of a face in texels.</summary>
	uint Size { get; }
}

/// <summary>Helpers for drawing models.</summary>
public static class ModelExtensions
{
	/// <summary>Submits every primitive of every node of <paramref name="model"/> placed at <paramref name="world"/>.</summary>
	public static void Draw(this IMeshBatch batch, IModel model, in Matrix4x4 world, bool castShadows = true, bool receiveShadows = true, uint layerMask = 1)
	{
		ArgumentNullException.ThrowIfNull(batch);
		ArgumentNullException.ThrowIfNull(model);
		var nodes = model.Nodes;
		for (var i = 0; i < nodes.Count; i++)
		{
			var node = nodes[i];
			if (node.Primitives.Length == 0) continue;
			var matrix = node.ModelMatrix * world;
			foreach (var primitive in node.Primitives)
			{
				var renderer = new MeshRenderer(primitive.Mesh, primitive.Material) { CastShadows = castShadows, ReceiveShadows = receiveShadows, LayerMask = layerMask };
				batch.Submit(renderer, matrix);
			}
		}
	}
}
