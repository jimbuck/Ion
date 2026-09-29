using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Ecs.Tests;

/// <summary>Records a creation in Update and looks at the world later in Update, in Render and in Last.</summary>
public sealed class Spawner(World world, Commands commands, Journal journal)
{
	[Update]
	public void Spawn(GameTime dt)
	{
		commands.Create(new Transform2D(new Vector2(dt.Frame, 0)), new Health(1));
		journal.Add($"update {world.Count<Health>()}");
	}

	[Update(Order = 100)]
	public void LaterInUpdate(GameTime dt) => journal.Add($"later {world.Count<Health>()}");

	[Render]
	public void Render(GameTime dt) => journal.Add($"render {world.Count<Health>()}");
}

public class CommandsTests
{
	[Fact]
	public void CommandsRecordedInAStageAreAppliedAtItsEnd()
	{
		using var host = Hosts.Ecs().Configure(s => s.AddJournal()).WithSystem<Spawner>();
		host.Step(2);

		Assert.Equal(["update 0", "later 0", "render 1", "update 1", "later 1", "render 2"], host.Get<Journal>().Entries);
	}

	[Fact]
	public void PlaybackAppliesCreatesAddsRemovesAndDestroysInOrder()
	{
		using var world = World.Create();
		var commands = new Commands(world);
		var existing = world.Create(new Health(5));
		var doomed = world.Create(new Health(9));

		var created = commands.Create(new Transform2D(new Vector2(3, 4)), new Health(2));
		commands.Add(existing, new Marker());
		commands.Set(existing, new Health(6));
		commands.Destroy(doomed);
		Assert.Equal(4, commands.Count);
		Assert.Equal(2, world.Size);

		commands.Flush();

		Assert.True(commands.IsEmpty);
		Assert.Equal(1, commands.Playbacks);
		Assert.Equal(2, world.Size);
		Assert.False(world.IsAlive(doomed));
		Assert.True(world.Has<Marker>(existing));
		Assert.Equal(6, world.Get<Health>(existing).Value);
		Assert.Equal(1, world.Count<Transform2D>());
		_ = created;

		commands.Flush();
		Assert.Equal(1, commands.Playbacks);
	}

	[Fact]
	public void SetParentWorksForEntitiesTheSameCommandsCreate()
	{
		using var world = World.Create();
		var commands = new Commands(world);
		var parent = world.Create(new Transform2D());

		var child = commands.Create(new Transform2D(new Vector2(1, 0)));
		commands.SetParent(child, parent);
		commands.Flush();

		var children = world.GetChildren(parent);
		Assert.Equal(1, children.Length);
		Assert.True(world.TryGetParent(children[0], out var found));
		Assert.Equal(parent, found);
		Assert.False(world.Has<PendingParent>(children[0]));

		commands.DestroyRecursive(parent);
		commands.Flush();
		Assert.Equal(0, world.Size);
	}

	[Fact]
	public void ClearDiscardsRecordedCommands()
	{
		using var world = World.Create();
		var commands = new Commands(world);
		commands.Create(new Health(1));
		commands.Clear();
		commands.Flush();
		Assert.Equal(0, world.Size);
		Assert.Equal(0, commands.Playbacks);
	}
}
