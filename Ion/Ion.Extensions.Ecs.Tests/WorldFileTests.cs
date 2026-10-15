using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Scenes;
using Ion.Testing;

namespace Ion.Extensions.Ecs.Tests;

/// <summary>Records what a scene's own Init step finds in its world.</summary>
public sealed class LevelProbe(World world, Journal journal)
{
	[Init]
	public void Init(GameTime dt) => journal.Add($"init {world.Size}");
}

public sealed class WorldFileTests : IDisposable
{
	private readonly string _assets = Directory.CreateTempSubdirectory("ion-worldfiles-").FullName;

	public void Dispose()
	{
		try { Directory.Delete(_assets, recursive: true); }
		catch (IOException) { }
	}

	/// <summary>A level: a "base" with a "turret" and its "barrel", and a free "rock".</summary>
	private void SaveLevel(string path, IWorldSerializer serializer)
	{
		using var world = World.Create();
		var hull = world.Create(new Transform2D(new Vector2(100, 50)), new EntityName("base"));
		var turret = world.Create(new Transform2D(new Vector2(10, 0)), new EntityName("turret"));
		var barrel = world.Create(new Transform2D(new Vector2(5, 0)), new EntityName("barrel"));
		world.Create(new Transform2D(new Vector2(-20, -20)), new EntityName("rock"));
		world.SetParent(turret, hull);
		world.SetParent(barrel, turret);

		var file = Path.Combine(_assets, path);
		Directory.CreateDirectory(Path.GetDirectoryName(file)!);
		using var stream = File.Create(file);
		serializer.Serialize(world, stream);
	}

	private IonTestHost Host(Action<IIonApplication> scenes) => Hosts.Ecs()
		.WithArgs($"--Ion:Storage:AssetsPath={_assets}")
		.Configure(services => services.AddEcsSerialization().AddJournal().AddTransient<LevelProbe>())
		.ConfigureApp(scenes);

	private static World SceneWorld(IonTestHost host)
	{
		var worlds = host.Get<EcsWorlds>();
		return worlds.Worlds.Single(w => w != worlds.Root);
	}

	[Fact]
	public void ASceneLoadsItsWorldFileWhenItIsEntered()
	{
		SaveLevel("levels/one.ion", new JsonWorldSerializer());
		using var host = Host(app =>
		{
			app.UseScene(1, scene => scene.UseEcs().LoadWorld("levels/one.ion").UseSystem<LevelProbe>());
			app.UseScene(2, scene => scene.UseEcs());
		});

		host.Step(2);
		var world = SceneWorld(host);
		Assert.Equal(4, world.Size);
		// The game's Init step already saw the entities.
		Assert.Equal(["init 4"], host.Get<Journal>().Entries);

		var names = new NameRegistry(world);
		var hull = names.Find("base");
		var turret = names.Find("turret");
		var barrel = names.Find("barrel");
		Assert.True(world.TryGetParent(turret, out var parent));
		Assert.Equal(hull, parent);
		Assert.Equal([barrel], world.GetChildren(turret).ToArray());
		Assert.False(world.TryGetParent(names.Find("rock"), out _));
		Assert.Equal(new Vector2(115, 50), world.Get<GlobalTransform2D>(barrel).Position);

		// Leaving and coming back loads a fresh copy into the new scene world.
		host.Events.EmitChangeScene(2);
		host.Step(2);
		Assert.Equal(0, SceneWorld(host).Size);
		host.Events.EmitChangeScene(1);
		host.Step(2);
		Assert.Equal(4, SceneWorld(host).Size);
		Assert.Equal(["init 4", "init 4"], host.Get<Journal>().Entries);
	}

	[Fact]
	public void SeveralFilesLoadInOrderAndBinaryFilesToo()
	{
		SaveLevel("one.ion", new JsonWorldSerializer());
		SaveLevel("two.ionb", new BinaryWorldSerializer());
		using var host = Host(app => app.UseScene(1, scene => scene.UseEcs().LoadWorld("one.ion").LoadWorld("two.ionb")));

		host.Step();
		var world = SceneWorld(host);
		Assert.Equal(8, world.Size);
		// Each file's base and turret have children: two trees per file.
		Assert.Equal(4, world.Count<Children>());
		Assert.Equal(4, world.Count<Parent>());
	}

	[Fact]
	public void AWorldFileIsAPrefabInstantiatedUnderAParent()
	{
		SaveLevel("prefabs/tank.ion", new BinaryWorldSerializer());
		using var host = Host(_ => { });
		var loader = host.Get<IEnumerable<IAssetLoader>>().OfType<IAssetLoader<WorldFile>>().Single();
		var tank = loader.Load("prefabs/tank.ion");
		Assert.True(tank.IsBinary);

		using var world = World.Create();
		var left = world.Create(new Transform2D(new Vector2(-1000, 0)));
		var right = world.Create(new Transform2D(new Vector2(1000, 0)));
		var a = tank.Instantiate(world, left);
		var b = tank.Instantiate(world, right);

		Assert.Equal(4, a.Count);
		Assert.Equal(10, world.Size);
		// The file's roots (the base and the rock) go under the parent, in saved order; the rest keeps its tree.
		Assert.Equal(2, world.GetChildren(left).Length);
		Assert.Equal(2, world.GetChildren(right).Length);
		new TransformPropagationSystem(world).Propagate();
		var barrels = new[] { a, b }.Select(set => set.Single(e => world.Get<EntityName>(e).Value == "barrel")).ToArray();
		Assert.Equal(new Vector2(-885, 50), world.Get<GlobalTransform2D>(barrels[0]).Position);
		Assert.Equal(new Vector2(1115, 50), world.Get<GlobalTransform2D>(barrels[1]).Position);
	}
}
