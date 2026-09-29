using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs.Tests;

public class SerializationTests
{
	public static TheoryData<string> Formats => ["json", "binary"];

	private static IWorldSerializer Serializer(string format) => format == "json" ? new JsonWorldSerializer() : new BinaryWorldSerializer();

	private static (Entity Root, Entity Child, Entity Camera) Populate(World world)
	{
		var root = world.Create(new Transform2D(new Vector2(10, 20), 0.5f, new Vector2(2, 3)), new EntityName("root"));
		var child = world.Create(new Transform2D(new Vector2(1, 2)), new EntityName("child"), new Hidden(),
			new SpriteAnimation([new RectangleF(0, 0, 8, 8), new RectangleF(8, 0, 8, 8)], 12, loop: false) { Time = 0.25f, Frame = 1 });
		world.SetParent(child, root);
		world.Create(new Transform(new Vector3(1, 2, 3), Quaternion.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f), new Vector3(4)));
		var camera = world.Create(new MainCamera(), new Camera2D { Position = new Vector2(5, 6), Zoom = 1.5f, Rotation = 0.25f });
		// Sprites hold a texture: not registered by default, so they are skipped.
		world.Create(new Sprite(Hosts.Texture()), new Visible());
		return (root, child, camera);
	}

	[Theory]
	[MemberData(nameof(Formats))]
	public void RoundTripsTheRegisteredComponentsAndTheHierarchy(string format)
	{
		using var source = World.Create();
		var (root, child, _) = Populate(source);
		var serializer = Serializer(format);

		using var stream = new MemoryStream();
		serializer.Serialize(source, stream);
		stream.Position = 0;

		using var target = World.Create();
		target.Create(new Health(1)); // an unrelated entity: saved positions must not assume empty worlds
		var loaded = serializer.Deserialize(stream, target);

		Assert.Equal(source.Size, loaded.Count);
		var names = new NameRegistry(target);
		var newRoot = names.Find("root");
		var newChild = names.Find("child");
		Assert.NotEqual(Entity.Null, newRoot);
		Assert.Equal(source.Get<Transform2D>(root), target.Get<Transform2D>(newRoot));
		Assert.Equal(source.Get<Transform2D>(child), target.Get<Transform2D>(newChild));
		Assert.True(target.Has<Hidden>(newChild));

		Assert.True(target.TryGetParent(newChild, out var parent));
		Assert.Equal(newRoot, parent);
		Assert.Equal([newChild], target.GetChildren(newRoot).ToArray());

		var animation = target.Get<SpriteAnimation>(newChild);
		Assert.Equal(source.Get<SpriteAnimation>(child).Frames, animation.Frames);
		Assert.Equal(0.25f, animation.Time);
		Assert.Equal(1, animation.Frame);
		Assert.False(animation.Loop);

		Assert.Equal(1, target.Count<Transform>());
		var transform = default(Transform);
		foreach (var e in loaded) if (target.Has<Transform>(e)) transform = target.Get<Transform>(e);
		Assert.Equal(new Transform(new Vector3(1, 2, 3), Quaternion.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f), new Vector3(4)), transform);

		var camera = loaded.Single(e => target.Has<MainCamera>(e));
		Assert.Equal(new Vector2(5, 6), target.Get<Camera2D>(camera).Position);
		Assert.Equal(1.5f, target.Get<Camera2D>(camera).Zoom);

		Assert.Equal(0, target.Count<Sprite>());
		Assert.Equal(1, target.Count<Visible>());

		// The loaded world propagates like the saved one.
		new TransformPropagationSystem(source).Propagate();
		new TransformPropagationSystem(target).Propagate();
		Assert.Equal(source.Get<GlobalTransform2D>(child).Matrix, target.Get<GlobalTransform2D>(newChild).Matrix);
	}

	[Fact]
	public void JsonNamesComponentsAndSavesEntityReferencesAsPositions()
	{
		using var world = World.Create();
		var parent = world.Create(new Transform2D(new Vector2(1, 2)), new EntityName("a"));
		var child = world.Create(new Transform2D(), new EntityName("b"));
		world.SetParent(child, parent);

		var json = new JsonWorldSerializer().ToJson(world);

		Assert.StartsWith("{\"format\":\"ion-world\",\"version\":1,\"entities\":[", json);
		Assert.Contains("\"Transform2D\":{\"Position\":[1,2],\"Rotation\":0,\"Scale\":[1,1]}", json);
		Assert.Contains("\"Parent\":{\"Value\":", json);
		Assert.Contains("\"Children\":[", json);
		Assert.Contains("\"EntityName\":{\"Value\":\"b\"}", json);
	}

	[Fact]
	public void UserComponentsRegisterWithTheirOwnCodecs()
	{
		var registry = ComponentSerializerRegistry.CreateDefault()
			.Add("Health", TestJsonContext.Default.Health,
				static (BinaryWriter writer, in Health value, EntityWriteMap _) => writer.Write(value.Value),
				static (reader, _) => new Health(reader.ReadInt32()));

		using var world = World.Create();
		world.Create(new Health(42), new EntityName("hero"));
		var bytes = new BinaryWorldSerializer(registry).ToBytes(world);

		using var target = World.Create();
		new BinaryWorldSerializer(registry).FromBytes(bytes, target);
		Assert.Equal(42, target.Get<Health>(new NameRegistry(target).Find("hero")).Value);

		// A reader without the registration skips the component and keeps the rest.
		using var partial = World.Create();
		new BinaryWorldSerializer().FromBytes(bytes, partial);
		Assert.Equal(0, partial.Count<Health>());
		Assert.Equal(1, partial.Count<EntityName>());

		Assert.Throws<InvalidOperationException>(() => registry.AddTag<Marker>("Health"));
	}

	[Fact]
	public void AddEcsSerializationRegistersTheSerializers()
	{
		var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
			.AddEcsSerialization(registry => registry.AddTag<Marker>("Marker"));
		using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

		Assert.IsType<BinaryWorldSerializer>(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IWorldSerializer>(provider));
		var registry = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ComponentSerializerRegistry>(provider);
		Assert.Contains("Marker", registry.Names);
		Assert.Contains("Transform2D", registry.Names);
	}

	[Fact]
	public void NameRegistryFollowsNameChanges()
	{
		using var world = World.Create();
		var names = new NameRegistry(world);
		var a = world.Create(new EntityName("a"));
		Assert.Equal(a, names.Find("a"));
		Assert.Equal("a", names.NameOf(a));

		world.Set(a, new EntityName("renamed"));
		Assert.Equal(Entity.Null, names.Find("a"));
		Assert.Equal(a, names.Find("renamed"));

		world.Destroy(a);
		Assert.Equal(Entity.Null, names.Find("renamed"));
		Assert.Null(names.NameOf(a));
	}
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(IncludeFields = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Health))]
internal sealed partial class TestJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
