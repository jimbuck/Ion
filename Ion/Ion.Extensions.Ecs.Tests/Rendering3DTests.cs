using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Assets;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;
using Ion.Extensions.Scenes;
using Ion.Testing;

namespace Ion.Extensions.Ecs.Tests;

internal static class Hosts3D
{
	/// <summary>A headless host with the ECS module, the CPU-only 3D renderer (no GPU) and the 3D extraction.</summary>
	public static IonTestHost Ecs3D(Action<Scene3DExtractionOptions>? configure = null) => new IonTestHost()
		.Configure(services => services.AddEcs().AddRendering3D().AddEcsRendering3D(configure))
		.ConfigureApp(app => app.UseEcs().UseRendering3D().UseEcsRendering3D());

	/// <summary>A camera 10 units up +Z, looking at the origin.</summary>
	public static Entity Camera(World world) => world.Create(Transform.LookAt(new Vector3(0, 0, 10), Vector3.Zero), new Camera());

	public static (MeshHandle Mesh, MaterialHandle Material) CubeAssets(IRenderer3D renderer) =>
		(renderer.CreateMesh(MeshPrimitives.Cube()), renderer.CreateMaterial(new PbrMaterial(Color.Red)));
}

/// <summary>A hand-built model: nodes, primitives and a name.</summary>
internal sealed class TestModel(string name, ModelNode[] nodes) : IModel
{
	public nint Id => 1;

	public string Name => name;

	public IReadOnlyList<ModelNode> Nodes => nodes;

	public IReadOnlyList<int> RootNodes { get; } = [.. Enumerable.Range(0, nodes.Length).Where(i => nodes[i].Parent < 0)];

	public IReadOnlyList<MeshHandle> Meshes => [];

	public IReadOnlyList<MaterialHandle> Materials => [];

	public IReadOnlyList<TextureHandle> Textures => [];

	public Aabb Bounds => default;

	public void Dispose()
	{
	}

	/// <summary>
	/// Four nodes: "body" (a root, one primitive, at x = 1) with children "arm" (two primitives, rotated and scaled) and
	/// "socket" (no primitive), and "base" (a second root, one primitive). Primitives use mesh ids 1..4.
	/// </summary>
	public static TestModel Robot()
	{
		static ModelPrimitive P(int id) => new(new MeshHandle(id), new MaterialHandle(1));
		ModelNode[] nodes =
		[
			new("body", -1, new Transform(new Vector3(1, 0, 0)), [P(1)]),
			new("arm", 0, new Transform(new Vector3(0, 2, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f), new Vector3(2)), [P(2), P(3)]),
			new("socket", 0, new Transform(new Vector3(0, -1, 0)), []),
			new("base", -1, new Transform(new Vector3(0, -3, 0)), [P(4)]),
		];
		ModelNode.Link(nodes);
		return new TestModel("robot", nodes);
	}
}

/// <summary>Records what the extraction submits.</summary>
internal sealed class RecordingMeshBatch : IMeshBatch
{
	public List<(MeshRenderer Renderer, Matrix4x4 World)> Submitted { get; } = [];

	public List<(Camera Camera, Matrix4x4 World)> Cameras { get; } = [];

	public List<(string Kind, Vector3 Value)> Lights { get; } = [];

	public List<SceneEnvironment> Environments { get; } = [];

	public void Clear()
	{
		Submitted.Clear();
		Cameras.Clear();
		Lights.Clear();
	}

	public void AddCamera(in Camera camera, in Matrix4x4 world) => Cameras.Add((camera, world));

	public void SetCamera(in Camera camera, in Transform transform) => throw new InvalidOperationException("The extraction adds cameras.");

	public void Submit(in MeshRenderer renderer, in Matrix4x4 world) => Submitted.Add((renderer, world));

	public void Draw(MeshHandle mesh, MaterialHandle material, in Matrix4x4 world) => Submitted.Add((new MeshRenderer(mesh, material), world));

	public void AddLight(in DirectionalLight light, in Matrix4x4 world) => Lights.Add(("directional", -new Vector3(world.M31, world.M32, world.M33)));

	public void AddLight(in DirectionalLight light, Vector3 direction) => Lights.Add(("directional", direction));

	public void AddLight(in PointLight light, Vector3 position) => Lights.Add(("point", position));

	public void AddLight(in SpotLight light, in Matrix4x4 world) => Lights.Add(("spot", world.Translation));

	public void SetEnvironment(in SceneEnvironment environment) => Environments.Add(environment);
}

public sealed class Scene3DProbe(World world, IRenderer3D renderer)
{
	[Init]
	public void Spawn(GameTime dt)
	{
		var (mesh, material) = Hosts3D.CubeAssets(renderer);
		Hosts3D.Camera(world);
		world.Create(new Transform(), new MeshRenderer(mesh, material));
		world.Create(new Transform(new Vector3(1, 0, 0)), new MeshRenderer(mesh, material));
	}
}

public class Scene3DExtractionTests
{
	[Fact]
	public void SubmitsMeshRenderersCamerasAndLightsToTheRenderer()
	{
		using var host = Hosts3D.Ecs3D();
		var world = host.Get<World>();
		var renderer = host.Get<Renderer3D>();
		var (mesh, material) = Hosts3D.CubeAssets(renderer);

		Hosts3D.Camera(world);
		for (var i = 0; i < 3; i++) world.Create(new Transform(new Vector3(i * 2 - 2, 0, 0)), new MeshRenderer(mesh, material));
		world.Create(new Transform(new Vector3(0, 1, 0)), new MeshRenderer(mesh, material), new Hidden());
		// Behind the camera: submitted, then culled by the renderer.
		world.Create(new Transform(new Vector3(0, 0, 50)), new MeshRenderer(mesh, material));
		world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-1, -1, -1), Vector3.UnitY)), new DirectionalLight());
		world.Create(new Transform(new Vector3(0, 2, 0)), new PointLight());
		world.Create(new Transform(new Vector3(0, 3, 2)), new SpotLight());
		world.Create(new Transform(new Vector3(0, 2, 0)), new PointLight(), new Hidden());

		host.Step();

		var stats = renderer.LastFrameStatistics;
		Assert.Equal(1, stats.Views);
		Assert.Equal(4, stats.Submitted);
		Assert.Equal(3, stats.Visible);
		Assert.Equal(1, stats.Culled);
		Assert.Equal(3, stats.Lights);
		Assert.True(renderer.ShadowActive);

		// Nothing persists in the renderer: the next frame extracts the same again.
		host.Step();
		Assert.Equal(4, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(1, renderer.LastFrameStatistics.Views);
	}

	[Fact]
	public void HiddenAndVisibleFilterTheMeshRenderers()
	{
		using var host = Hosts3D.Ecs3D(options => options.RequireVisible = true);
		var world = host.Get<World>();
		var renderer = host.Get<Renderer3D>();
		var (mesh, material) = Hosts3D.CubeAssets(renderer);
		Hosts3D.Camera(world);
		world.Create(new Transform(), new MeshRenderer(mesh, material));
		world.Create(new Transform(), new MeshRenderer(mesh, material), new Visible());
		world.Create(new Transform(), new MeshRenderer(mesh, material), new Visible(), new Hidden());

		host.Step();

		Assert.Equal(1, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(1, renderer.LastFrameStatistics.Visible);
	}

	[Fact]
	public void HiddenCamerasAreNotAddedAndMaskedLayersAreNotVisible()
	{
		using var host = Hosts3D.Ecs3D();
		var world = host.Get<World>();
		var renderer = host.Get<Renderer3D>();
		var (mesh, material) = Hosts3D.CubeAssets(renderer);
		world.Create(Transform.LookAt(new Vector3(0, 0, 10), Vector3.Zero), new Camera { CullingMask = 1 });
		world.Create(Transform.LookAt(new Vector3(0, 0, -10), Vector3.Zero), new Camera(), new Hidden());
		world.Create(new Transform(), new MeshRenderer(mesh, material));
		world.Create(new Transform(), new MeshRenderer(mesh, material) { LayerMask = 2 });

		host.Step();

		Assert.Equal(1, renderer.LastFrameStatistics.Views);
		Assert.Equal(2, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(1, renderer.LastFrameStatistics.Visible);
	}

	[Fact]
	public void EntitiesCreatedThisFrameAreDrawnWhereTheyAre()
	{
		using var host = Hosts3D.Ecs3D().ConfigureApp(app => app.Update((GameTime dt, Commands commands, IRenderer3D renderer) =>
		{
			if (dt.Frame != 0) return;
			var (mesh, material) = Hosts3D.CubeAssets(renderer);
			commands.Create(Transform.LookAt(new Vector3(0, 0, 10), Vector3.Zero), new Camera());
			commands.Create(new Transform(new Vector3(0, 0, 30)), new MeshRenderer(mesh, material));
			commands.Create(new Transform(new Vector3(1, 0, 0)), new MeshRenderer(mesh, material));
		}));

		host.Step();

		var stats = host.Get<IRenderer3D>().LastFrameStatistics;
		Assert.Equal(2, stats.Submitted);
		Assert.Equal(1, stats.Visible);
		Assert.Equal(1, stats.Culled);
	}

	[Fact]
	public void PassesTheWorldsEnvironmentWhenItChanges()
	{
		using var world = World.Create();
		var batch = new RecordingMeshBatch();
		var extraction = new Scene3DExtractionSystem(world, batch);
		var time = new GameTime();

		extraction.ExtractAll(time);
		Assert.Empty(batch.Environments);

		var sky = new SceneEnvironment { AmbientColor = Color.Blue, AmbientIntensity = 0.5f };
		world.SetEnvironment(sky);
		Assert.True(world.TryGetEnvironment(out var stored));
		Assert.Equal(sky.AmbientColor, stored.AmbientColor);

		extraction.ExtractAll(time);
		extraction.ExtractAll(time);
		Assert.Single(batch.Environments);
		Assert.Equal(0.5f, batch.Environments[0].AmbientIntensity);

		world.SetEnvironment(sky with { AmbientIntensity = 2f });
		Assert.Equal(1, world.Count<SceneEnvironment>());
		extraction.ExtractAll(time);
		Assert.Equal(2, batch.Environments.Count);
		Assert.Equal(2f, batch.Environments[1].AmbientIntensity);

		Assert.True(world.RemoveEnvironment());
		Assert.Equal(0, world.Size);
		extraction.ExtractAll(time);
		Assert.Equal(3, batch.Environments.Count);
		Assert.Equal(new SceneEnvironment().AmbientIntensity, batch.Environments[2].AmbientIntensity);
		Assert.Equal(3, extraction.LastFrame.EnvironmentUpdates);
	}

	[Fact]
	public void TheEnvironmentReachesTheRenderer()
	{
		using var host = Hosts3D.Ecs3D();
		var world = host.Get<World>();
		world.SetEnvironment(new SceneEnvironment { AmbientColor = Color.Green, AmbientIntensity = 0.25f });
		host.Step();
		Assert.Equal(Color.Green, host.Get<Renderer3D>().Environment.AmbientColor);
		Assert.Equal(0.25f, host.Get<Renderer3D>().Environment.AmbientIntensity);
	}

	[Fact]
	public void LightsFollowTheirEntitiesWorldTransforms()
	{
		using var world = World.Create();
		var batch = new RecordingMeshBatch();
		var extraction = new Scene3DExtractionSystem(world, batch);
		var parent = world.Create(new Transform(new Vector3(10, 0, 0)));
		var point = world.Create(new Transform(new Vector3(0, 2, 0)), new PointLight());
		var spot = world.Create(new Transform(new Vector3(0, 0, 3)), new SpotLight());
		// Pointing down: -Z of the entity is -Y of the world.
		world.Create(new Transform(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2)), new DirectionalLight());
		world.SetParent(point, parent);
		world.SetParent(spot, parent);

		new TransformPropagationSystem(world).Propagate();
		extraction.ExtractAll(new GameTime());

		Assert.Equal(3, extraction.LastFrame.Lights);
		Assert.Contains(("point", new Vector3(10, 2, 0)), batch.Lights);
		Assert.Contains(("spot", new Vector3(10, 0, 3)), batch.Lights);
		var directional = batch.Lights.Single(l => l.Kind == "directional").Value;
		Assert.True(Vector3.Distance(-Vector3.UnitY, directional) < 1e-5f, directional.ToString());
	}

	[Fact]
	public void DoesNotAllocatePerFrame()
	{
		using var world = World.Create();
		using var renderer = new Renderer3D(null);
		var (mesh, material) = Hosts3D.CubeAssets(renderer);
		Hosts3D.Camera(world);
		for (var i = 0; i < 2_000; i++) world.Create(new Transform(new Vector3(i % 50, i / 50, 0)), new MeshRenderer(mesh, material));
		world.Create(new Transform(), new DirectionalLight());
		world.Create(new Transform(), new PointLight());
		world.Create(new Transform(), new SpotLight());
		world.SetEnvironment(new SceneEnvironment());
		var propagation = new TransformPropagationSystem(world);
		var extraction = new Scene3DExtractionSystem(world, renderer);
		var time = new GameTime();

		void Frame()
		{
			propagation.Propagate();
			renderer.BeginFrame();
			extraction.ExtractAll(time);
			renderer.EndFrame();
		}

		for (var i = 0; i < 3; i++) Frame();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 10; i++) Frame();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(2_000, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(new Scene3DExtractionStats(2_000, 1, 3, 1), extraction.LastFrame);
	}

	[Fact]
	public void ScenesExtractTheirOwnWorld()
	{
		using var host = new IonTestHost()
			.Configure(services => services.AddEcs().AddRendering3D().AddEcsRendering3D().AddTransient<Scene3DProbe>())
			.ConfigureApp(app =>
			{
				app.UseEcs().UseRendering3D().UseEcsRendering3D();
				app.UseScene(1, scene => scene.UseEcs().UseEcsRendering3D().UseSystem<Scene3DProbe>());
			});

		var root = host.Get<World>();
		root.Create(new Transform(new Vector3(-1, 0, 0)), new MeshRenderer(new MeshHandle(1), new MaterialHandle(1)));
		host.Step(2);

		// The root world's mesh and the scene's two, one camera (the scene's).
		var stats = host.Get<IRenderer3D>().LastFrameStatistics;
		Assert.Equal(3, stats.Submitted);
		Assert.Equal(1, stats.Views);
	}
}

public class ModelSpawnTests
{
	[Fact]
	public void SpawnsOneEntityPerNodeWithTheModelsHierarchy()
	{
		using var world = World.Create();
		var model = TestModel.Robot();

		var root = world.SpawnModel(model, new Transform(new Vector3(0, 0, -5)));

		Assert.Equal("robot", world.Get<EntityName>(root).Value);
		Assert.Equal(new Vector3(0, 0, -5), world.Get<Transform>(root).Position);
		Assert.False(world.Has<MeshRenderer>(root));

		var roots = world.GetChildren(root).ToArray();
		Assert.Equal(["body", "base"], roots.Select(e => world.Get<EntityName>(e).Value));
		var body = roots[0];
		Assert.Equal(new MeshHandle(1), world.Get<MeshRenderer>(body).Mesh);
		Assert.Equal(new Vector3(1, 0, 0), world.Get<Transform>(body).Position);
		Assert.Equal(new MeshHandle(4), world.Get<MeshRenderer>(roots[1]).Mesh);

		var bodyChildren = world.GetChildren(body).ToArray();
		Assert.Equal(["arm", "socket"], bodyChildren.Select(e => world.Get<EntityName>(e).Value));
		var arm = bodyChildren[0];
		Assert.False(world.Has<MeshRenderer>(arm));
		var primitives = world.GetChildren(arm).ToArray();
		Assert.Equal(2, primitives.Length);
		Assert.Equal([new MeshHandle(2), new MeshHandle(3)], primitives.Select(e => world.Get<MeshRenderer>(e).Mesh));
		Assert.All(primitives, e => Assert.Equal(Transform.Identity, world.Get<Transform>(e)));
		Assert.All(primitives, e => Assert.False(world.Has<EntityName>(e)));
		var socket = bodyChildren[1];
		Assert.False(world.Has<MeshRenderer>(socket));
		Assert.Empty(world.GetChildren(socket).ToArray());

		// Root, 4 nodes and 2 primitive entities.
		Assert.Equal(7, world.Size);
		Assert.Equal(4, world.Count<MeshRenderer>());
	}

	[Fact]
	public void PropagationGivesEveryPrimitiveTheModelMatrixTimesTheRoot()
	{
		using var world = World.Create();
		var model = TestModel.Robot();
		var placement = new Transform(new Vector3(3, 1, -2), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f), new Vector3(0.5f));
		world.SpawnModel(model, placement, new ModelSpawnOptions { CastShadows = false, LayerMask = 4 });
		var batch = new RecordingMeshBatch();
		var extraction = new Scene3DExtractionSystem(world, batch);

		new TransformPropagationSystem(world).Propagate();
		extraction.ExtractAll(new GameTime());

		// What the immediate-mode ModelExtensions.Draw submits for the same model and placement.
		var expected = new RecordingMeshBatch();
		expected.Draw(model, placement.ToMatrix());

		Assert.Equal(4, batch.Submitted.Count);
		foreach (var (renderer, matrix) in expected.Submitted)
		{
			var actual = batch.Submitted.Single(s => s.Renderer.Mesh == renderer.Mesh);
			Assert.False(actual.Renderer.CastShadows);
			Assert.True(actual.Renderer.ReceiveShadows);
			Assert.Equal(4u, actual.Renderer.LayerMask);
			AssertClose(matrix, actual.World);
		}
	}

	[Fact]
	public void CommandsSpawnTheModelAtPlayback()
	{
		using var host = Hosts3D.Ecs3D();
		var model = TestModel.Robot();
		var spawned = Entity.Null;
		host.ConfigureApp(app => app.Update((GameTime dt, Commands commands) =>
		{
			if (dt.Frame == 0) spawned = commands.SpawnModel(model, new Transform(new Vector3(0, 0, -5)), new ModelSpawnOptions { Names = false });
		}));
		var world = host.Get<World>();
		Hosts3D.Camera(world);

		host.Step();

		Assert.Equal(1 + 7, world.Size);
		Assert.Equal(8, world.Count<GlobalTransform>());
		Assert.Equal(4, world.Count<MeshRenderer>());
		Assert.Equal(0, world.Count<EntityName>());
		Assert.Equal(0, world.Count<PendingModel>());
		// Submitted in the frame of the spawn (the meshes are fake ids: nothing is visible).
		Assert.Equal(4, host.Get<IRenderer3D>().LastFrameStatistics.Submitted);
	}

	[Fact]
	public void CommandsCanParentASpawnedModel()
	{
		using var world = World.Create();
		var commands = new Commands(world);
		var anchor = world.Create(new Transform(new Vector3(5, 0, 0)));

		var root = commands.SpawnModel(TestModel.Robot());
		commands.SetParent(root, anchor);
		commands.Flush();

		var spawned = world.GetChildren(anchor).ToArray();
		Assert.Single(spawned);
		Assert.Equal("robot", world.Get<EntityName>(spawned[0]).Value);
		Assert.Equal(2, world.GetChildren(spawned[0]).Length);
	}

	[Fact]
	public void SetHiddenHidesAModelAndDestroyRecursiveRemovesIt()
	{
		using var host = Hosts3D.Ecs3D();
		var world = host.Get<World>();
		var renderer = host.Get<Renderer3D>();
		Hosts3D.Camera(world);
		var root = world.SpawnModel(TestModel.Robot());

		host.Step();
		Assert.Equal(4, renderer.LastFrameStatistics.Submitted);

		world.SetHidden(root, true);
		host.Step();
		Assert.Equal(0, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(7, world.Count<Hidden>());

		world.SetHidden(root, false);
		host.Step();
		Assert.Equal(4, renderer.LastFrameStatistics.Submitted);

		world.DestroyRecursive(root);
		host.Step();
		Assert.Equal(0, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(1, world.Size);
	}

	private static void AssertClose(Matrix4x4 expected, Matrix4x4 actual)
	{
		for (var r = 0; r < 4; r++)
		{
			for (var c = 0; c < 4; c++) Assert.Equal(expected[r, c], actual[r, c], 4);
		}
	}
}
