using System.Text;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

namespace Ion.Extensions.Ecs.Tests;

/// <summary>The 3D components saved and loaded, with their renderer handles by name.</summary>
public class Serialization3DTests
{
	public static TheoryData<string> Formats => ["json", "binary"];

	private static IWorldSerializer Serializer(string format, ComponentSerializerRegistry registry) =>
		format == "json" ? new JsonWorldSerializer(registry) : new BinaryWorldSerializer(registry);

	private static T RoundTrip<T>(string format, ComponentSerializerRegistry registry, T component, ComponentSerializerRegistry? loadWith = null) where T : struct
	{
		using var source = World.Create();
		source.Create(component);
		using var stream = new MemoryStream();
		Serializer(format, registry).Serialize(source, stream);
		stream.Position = 0;
		using var target = World.Create();
		var loaded = Serializer(format, loadWith ?? registry).Deserialize(stream, target);
		return target.Get<T>(Assert.Single(loaded));
	}

	private static ComponentSerializerRegistry Named()
	{
		var registry = ComponentSerializerRegistry.CreateDefault();
		registry.Handles.Name(new MeshHandle(5), "cube").Name(new MaterialHandle(9), "red").Name(new TextureHandle(3), "sky").Name(new RenderTargetHandle(2), "minimap");
		return registry;
	}

	[Theory]
	[MemberData(nameof(Formats))]
	public void EachComponentRoundTrips(string format)
	{
		var registry = Named();

		var renderer = new MeshRenderer(new MeshHandle(5), new MaterialHandle(9)) { CastShadows = false, LayerMask = 6 };
		Assert.Equal(renderer, RoundTrip(format, registry, renderer));

		var camera = new Camera
		{
			Projection = ProjectionKind.Orthographic,
			FieldOfView = 0.9f,
			OrthographicSize = 7f,
			Near = 0.5f,
			Far = 300f,
			Viewport = new RectangleF(0.5f, 0, 0.5f, 1),
			ClearColor = new Color(0.1f, 0.2f, 0.3f, 1f),
			Clear = CameraClear.Skybox,
			Priority = 3,
			Target = new RenderTargetHandle(2),
			CullingMask = 0b1010,
		};
		Assert.Equal(camera, RoundTrip(format, registry, camera));

		var sun = new DirectionalLight(new Color(1f, 0.9f, 0.8f, 1f), 3f, castShadows: false) { ShadowBias = 0.002f };
		Assert.Equal(sun, RoundTrip(format, registry, sun));
		var lamp = new PointLight(new Color(0.2f, 1f, 0.4f, 1f), 2.5f, 12f);
		Assert.Equal(lamp, RoundTrip(format, registry, lamp));
		var spot = new SpotLight { Color = Color.Red, Intensity = 4f, Range = 20f, InnerConeAngle = 0.2f, OuterConeAngle = 0.4f };
		Assert.Equal(spot, RoundTrip(format, registry, spot));

		var environment = new SceneEnvironment { AmbientColor = new Color(0.3f, 0.3f, 0.4f, 1f), AmbientIntensity = 0.5f, Skybox = new TextureHandle(3), SkyboxIntensity = 0.8f };
		Assert.Equal(environment, RoundTrip(format, registry, environment));
	}

	[Theory]
	[MemberData(nameof(Formats))]
	public void UnnamedHandlesKeepTheirIdsAndNoneStaysNone(string format)
	{
		var registry = ComponentSerializerRegistry.CreateDefault();
		Assert.Equal(new MeshRenderer(new MeshHandle(42), MaterialHandle.None), RoundTrip(format, registry, new MeshRenderer(new MeshHandle(42), MaterialHandle.None)));
		Assert.Equal(TextureHandle.None, RoundTrip(format, registry, new SceneEnvironment()).Skybox);
	}

	[Theory]
	[MemberData(nameof(Formats))]
	public void LoadingANameWithoutAHandleFailsWithAMessage(string format)
	{
		var thrown = Assert.Throws<InvalidDataException>(() => RoundTrip(format, Named(), new MeshRenderer(new MeshHandle(5), new MaterialHandle(9)), loadWith: ComponentSerializerRegistry.CreateDefault()));
		Assert.Contains("'cube'", thrown.Message);
	}

	[Fact]
	public void TheJsonFormStoresNames()
	{
		var registry = Named();
		using var world = World.Create();
		world.Create(new MeshRenderer(new MeshHandle(5), new MaterialHandle(9)), new PointLight());
		using var stream = new MemoryStream();
		new JsonWorldSerializer(registry).Serialize(world, stream);
		var json = Encoding.UTF8.GetString(stream.ToArray());
		Assert.Contains("\"Mesh\":\"cube\"", json.Replace(" ", ""));
		Assert.Contains("\"Material\":\"red\"", json.Replace(" ", ""));
		Assert.Contains("PointLight", json);
	}

	[Fact]
	public void ASaved3DSceneLoadsIntoAnotherRendererAndRenders()
	{
		// The scene is saved from one host (one renderer's handles) and loaded into another, whose renderer gives the same
		// meshes and materials other ids: the names connect them.
		static (MeshHandle Cube, MeshHandle Ball, MaterialHandle Red) Assets(Ion.Testing.IonTestHost host, bool offset)
		{
			var renderer = host.Get<Renderer3D>();
			if (offset) renderer.CreateMesh(MeshPrimitives.Plane());   // shifts this renderer's ids
			var cube = renderer.CreateMesh(MeshPrimitives.Cube());
			var ball = renderer.CreateMesh(MeshPrimitives.Sphere());
			var red = renderer.CreateMaterial(new PbrMaterial(Color.Red));
			host.Get<RenderHandleNames>().Name(cube, "cube").Name(ball, "ball").Name(red, "red");
			return (cube, ball, red);
		}

		static Ion.Testing.IonTestHost Host() => Hosts3D.Ecs3D().Configure(s => s.AddEcsSerialization());

		byte[] saved;
		using (var first = Host())
		{
			var world = first.Get<World>();
			var (cube, ball, red) = Assets(first, offset: false);
			Hosts3D.Camera(world);
			world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-1, -1, -1), Vector3.UnitY)), new DirectionalLight(Color.White, 2f));
			world.SetEnvironment(new SceneEnvironment { AmbientIntensity = 0.3f });
			var table = world.Create(new Transform(new Vector3(0, -1, 0)), new MeshRenderer(cube, red), new EntityName("table"));
			var top = world.Create(new Transform(new Vector3(0, 1, 0)), new MeshRenderer(ball, red), new EntityName("ball"));
			var lamp = world.Create(new Transform(new Vector3(0, 2, 0)), new PointLight(Color.White, 1f, 5f));
			world.SetParent(top, table);
			world.SetParent(lamp, top);
			first.Step();
			Assert.Equal(2, first.Get<Renderer3D>().LastFrameStatistics.Submitted);

			using var stream = new MemoryStream();
			first.Get<IWorldSerializer>().Serialize(world, stream);
			saved = stream.ToArray();
		}

		using var second = Host();
		var (cube2, ball2, red2) = Assets(second, offset: true);
		Assert.NotEqual(1, cube2.Id);
		var target = second.Get<World>();
		second.Get<IWorldSerializer>().Deserialize(new MemoryStream(saved), target);
		second.Step();

		var stats = second.Get<Renderer3D>().LastFrameStatistics;
		Assert.Equal(1, stats.Views);
		Assert.Equal(2, stats.Submitted);
		Assert.Equal(2, stats.Lights);
		Assert.Equal(0.3f, second.Get<Renderer3D>().Environment.AmbientIntensity);

		var names = new NameRegistry(target);
		Assert.Equal(new MeshRenderer(cube2, red2), target.Get<MeshRenderer>(names.Find("table")));
		Assert.Equal(new MeshRenderer(ball2, red2), target.Get<MeshRenderer>(names.Find("ball")));
		Assert.True(target.TryGetParent(names.Find("ball"), out var parent));
		Assert.Equal(names.Find("table"), parent);
	}
}
