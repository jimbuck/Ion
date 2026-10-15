using Arch.Core;

using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

namespace Ion.Extensions.Ecs.Tests;

/// <summary>A hand-built skinned model: one node with one skinned primitive, a one-joint skeleton and a sliding clip.</summary>
internal sealed class SkinnedTestModel(MeshHandle mesh) : IModel
{
	private readonly ModelNode[] _nodes = Link(new ModelNode("body", -1, new Transform(new Vector3(0, 0.5f, 0)), [new ModelPrimitive(mesh, MaterialHandle.None, Skin: 0)]));

	public static readonly Skeleton Skeleton = new("one", [new SkeletonJoint("bone", -1, Transform.Identity, Matrix4x4.Identity)]);

	/// <summary>Slides the joint from x = 0 to x = 4 over one second.</summary>
	public static readonly AnimationClip Slide = new("slide", [new AnimationChannel(0, AnimationPath.Translation, AnimationInterpolation.Linear, [0f, 1f], [Vector4.Zero, new Vector4(4, 0, 0, 0)])]);

	private static ModelNode[] Link(params ModelNode[] nodes)
	{
		ModelNode.Link(nodes);
		return nodes;
	}

	public nint Id => 2;

	public string Name => "skinned";

	public IReadOnlyList<ModelNode> Nodes => _nodes;

	public IReadOnlyList<int> RootNodes { get; } = [0];

	public IReadOnlyList<MeshHandle> Meshes => [mesh];

	public IReadOnlyList<MaterialHandle> Materials => [];

	public IReadOnlyList<TextureHandle> Textures => [];

	public Aabb Bounds => default;

	public IReadOnlyList<Skeleton> Skins => [Skeleton];

	public IReadOnlyList<AnimationClip> Animations => [Slide];

	public int SkinOf(AnimationClip clip) => clip == Slide ? 0 : -1;

	public void Dispose()
	{
	}

	/// <summary>A unit cube following joint 0.</summary>
	public static MeshData Cube()
	{
		var data = MeshPrimitives.Cube();
		data.Joints = new JointIndices[data.VertexCount];
		data.Weights = [.. data.Positions.Select(_ => Vector4.UnitX)];
		return data;
	}
}

/// <summary>Records the skinned submissions (with a copy of each palette) besides the rigid ones.</summary>
internal sealed class SkinRecordingBatch : IMeshBatch
{
	public List<(MeshRenderer Renderer, Matrix4x4 World)> Rigid { get; } = [];

	public List<(MeshRenderer Renderer, Matrix4x4 World, Matrix4x4[] Joints)> Skinned { get; } = [];

	public void Submit(in MeshRenderer renderer, in Matrix4x4 world) => Rigid.Add((renderer, world));

	public void Submit(in MeshRenderer renderer, in Matrix4x4 world, ReadOnlySpan<Matrix4x4> joints) => Skinned.Add((renderer, world, joints.ToArray()));

	public void AddCamera(in Camera camera, in Matrix4x4 world) { }

	public void SetCamera(in Camera camera, in Transform transform) { }

	public void Draw(MeshHandle mesh, MaterialHandle material, in Matrix4x4 world) => Rigid.Add((new MeshRenderer(mesh, material), world));

	public void AddLight(in DirectionalLight light, in Matrix4x4 world) { }

	public void AddLight(in DirectionalLight light, Vector3 direction) { }

	public void AddLight(in PointLight light, Vector3 position) { }

	public void AddLight(in SpotLight light, in Matrix4x4 world) { }

	public void SetEnvironment(in SceneEnvironment environment) { }
}

public class SkeletalAnimationTests
{
	private static (Ion.Testing.IonTestHost Host, World World, Entity Root, Entity Body) Spawn(Action<Scene3DExtractionOptions>? options = null)
	{
		var host = Hosts3D.Ecs3D(options);
		var world = host.Get<World>();
		var model = new SkinnedTestModel(host.Get<Renderer3D>().CreateMesh(SkinnedTestModel.Cube()));
		var root = world.SpawnModel(model, new Transform(new Vector3(0, 0, -5)));
		var body = Assert.Single(world.GetChildren(root).ToArray());
		Hosts3D.Camera(world);
		return (host, world, root, body);
	}

	[Fact]
	public void ASkinnedModelSpawnsWithAnAnimatorOnItsRootAndSkinnedMeshesPointingAtIt()
	{
		var (host, world, root, body) = Spawn();
		using (host)
		{
			var animator = world.Get<Animator>(root);
			Assert.Same(SkinnedTestModel.Skeleton, animator.Player.Skeleton);
			Assert.Null(animator.Player.Clip);
			Assert.True(world.Has<MeshRenderer>(body));
			Assert.Equal(root, world.Get<SkinnedMesh>(body).Animator);
		}
	}

	[Fact]
	public void TheAnimatorAdvancesEveryUpdateAndTheExtractionSubmitsThePoseAtTheRoot()
	{
		var (host, world, root, body) = Spawn();
		using (host)
		{
			world.Get<Animator>(root).Player.Play(SkinnedTestModel.Slide, loop: false);
			host.Step(15);
			var player = world.Get<Animator>(root).Player;
			Assert.Equal(15 * (float)host.FrameTime.TotalSeconds, player.Time, 3);

			var batch = new SkinRecordingBatch();
			new Scene3DExtractionSystem(world, batch).ExtractAll(new GameTime());
			Assert.Empty(batch.Rigid);
			var (_, at, joints) = Assert.Single(batch.Skinned);
			// At the root's world matrix (the palette is in model space), not the body node's (0.5 up).
			Assert.Equal(new Vector3(0, 0, -5), at.Translation);
			Assert.Equal(player.Time * 4, joints[0].Translation.X, 3);

			// The renderer drew it: one submission, inside the camera's view.
			var stats = host.Get<IRenderer3D>().LastFrameStatistics;
			Assert.Equal(1, stats.Submitted);
			Assert.Equal(1, stats.Visible);
		}
	}

	[Fact]
	public void WithoutItsAnimatorASkinnedMeshIsDrawnRigidAtItsOwnPlace()
	{
		var (host, world, root, body) = Spawn();
		using (host)
		{
			world.Remove<Animator>(root);
			host.Step();
			var batch = new SkinRecordingBatch();
			new Scene3DExtractionSystem(world, batch).ExtractAll(new GameTime());
			Assert.Empty(batch.Skinned);
			var (_, at) = Assert.Single(batch.Rigid);
			Assert.Equal(new Vector3(0, 0.5f, -5), at.Translation);
		}
	}

	[Fact]
	public void HiddenAndNotVisibleSkinnedMeshesAreNotSubmitted()
	{
		var (host, world, root, body) = Spawn(o => o.RequireVisible = true);
		using (host)
		{
			host.Step();
			var batch = new SkinRecordingBatch();
			var extraction = new Scene3DExtractionSystem(world, batch, new Scene3DExtractionOptions { RequireVisible = true });
			extraction.ExtractAll(new GameTime());
			Assert.Empty(batch.Skinned);

			world.Add(body, new Visible());
			host.Step();
			extraction.ExtractAll(new GameTime());
			Assert.Single(batch.Skinned);
			Assert.Equal(1, extraction.LastFrame.MeshRenderers);

			batch.Skinned.Clear();
			world.SetHidden(root, true);
			host.Step();
			extraction.ExtractAll(new GameTime());
			Assert.Empty(batch.Skinned);
		}
	}
}
