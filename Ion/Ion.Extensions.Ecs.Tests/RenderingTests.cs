using Arch.Core;

using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs.Tests;

public class SpriteAnimationTests
{
	private static readonly RectangleF[] Frames = [new(0, 0, 8, 8), new(8, 0, 8, 8), new(16, 0, 8, 8), new(24, 0, 8, 8)];

	[Fact]
	public void AdvancesFramesAtTheirRateAndLoops()
	{
		using var host = Hosts.Ecs();
		var world = host.Get<World>();
		var entity = world.Create(new Transform2D(), new Sprite(Hosts.Texture()), new SpriteAnimation(Frames, framesPerSecond: 10));

		// 60 fps: a new frame every 6 frames, back to the first after 24.
		host.Step(5);
		Assert.Equal(Frames[0], world.Get<Sprite>(entity).Source);
		host.Step(1);
		Assert.Equal(Frames[1], world.Get<Sprite>(entity).Source);
		host.Step(12);
		Assert.Equal(Frames[3], world.Get<Sprite>(entity).Source);
		host.Step(6);
		Assert.Equal(Frames[0], world.Get<Sprite>(entity).Source);
	}

	[Fact]
	public void StopsOnTheLastFrameWhenNotLooping()
	{
		var animation = new SpriteAnimation(Frames, framesPerSecond: 4, loop: false);
		Assert.True(animation.Advance(0.3f, out var source));
		Assert.Equal(Frames[1], source);
		animation.Advance(5f, out source);
		Assert.Equal(Frames[3], source);
		Assert.True(animation.IsFinished);

		var paused = new SpriteAnimation(Frames, 4) { Paused = true };
		paused.Advance(1f, out source);
		Assert.Equal(Frames[0], source);
	}
}

public class SpriteExtractionTests
{
	[Fact]
	public void DrawsVisibleSpritesByDepthThroughTheSpriteBatch()
	{
		using var host = Hosts.Ecs(rendering: true);
		var world = host.Get<World>();
		var texture = Hosts.Texture();
		world.Create(new Transform2D(new Vector2(100, 100)), new Sprite(texture, depth: 2) { Color = Color.Red });
		world.Create(new Transform2D(new Vector2(200, 100)), new Sprite(texture, depth: 0));
		world.Create(new Transform2D(new Vector2(300, 100)), new Sprite(texture, depth: 1));
		world.Create(new Transform2D(new Vector2(400, 100)), new Sprite(texture), new Hidden());
		world.Create(new Transform2D(new Vector2(-500, -500)), new Sprite(texture));
		world.Create(new Transform2D(new Vector2(500, 100)), new Sprite());

		host.Step();

		var frame = host.SpriteBatch.LastFrame;
		Assert.Equal(3, frame.Sprites);
		var draws = frame.Commands.Where(c => c.Kind == SpriteBatchCommandKind.Sprite).ToList();
		Assert.Equal([new Vector2(200, 100), new Vector2(300, 100), new Vector2(100, 100)], draws.Select(d => d.Position));
		Assert.Equal([0f, 1f, 2f], draws.Select(d => d.Depth));
		Assert.Equal(new Vector2(32, 16), draws[0].Size);
		Assert.Equal(Color.Red, draws[2].Color);

		// Every sprite got its bounds from the Render pass of the propagation.
		Assert.Equal(6, world.Count<Aabb2D>());
	}

	[Fact]
	public void SpritesCreatedThisFrameAreDrawnWhereTheyAre()
	{
		using var host = Hosts.Ecs(rendering: true).ConfigureApp(app => app.Update((GameTime dt, World world) =>
		{
			if (dt.Frame == 0) world.Create(new Transform2D(new Vector2(64, 32)), new Sprite(Hosts.Texture()));
		}));

		host.Step();

		var draw = Assert.Single(host.SpriteBatch.LastFrame.Commands);
		Assert.Equal(new Vector2(64, 32), draw.Position);
	}

	[Fact]
	public void UsesTheMainCameraForTheTransformAndTheCulling()
	{
		using var host = Hosts.Ecs();
		using var world = World.Create();
		var batch = new NullSpriteBatch();
		var window = host.Window;
		window.Size = new Vector2(800, 600);
		var system = new SpriteExtractionSystem(world, batch, window);
		var propagation = new TransformPropagationSystem(world);
		var texture = Hosts.Texture();

		world.Create(new Transform2D(new Vector2(1000, 1000)), new Sprite(texture));
		world.Create(new Transform2D(new Vector2(100, 100)), new Sprite(texture));
		propagation.Propagate();

		batch.Begin();
		system.Extract(new GameTime());
		batch.End();
		Assert.Equal(new SpriteExtractionStats(1, 1, false), system.LastFrame);

		// A camera centered on (1000, 1000) at zoom 2 sees (800, 850) to (1200, 1150).
		var camera = new Camera2D { Position = new Vector2(1000, 1000), Zoom = 2 };
		world.Create(new MainCamera(), camera);
		batch.Begin();
		system.Extract(new GameTime());
		batch.End();
		Assert.Equal(new SpriteExtractionStats(1, 1, true), system.LastFrame);
		Assert.Equal(new Vector2(1000, 1000), batch.LastFrame.Commands.Single().Position);
	}

	[Fact]
	public void CanRequireTheVisibleTag()
	{
		using var host = Hosts.Ecs();
		using var world = World.Create();
		var batch = new NullSpriteBatch();
		var system = new SpriteExtractionSystem(world, batch, host.Window, new SpriteExtractionOptions { RequireVisible = true, Cull = false });
		world.Create(new Transform2D(new Vector2(10, 10)), new Sprite(Hosts.Texture()), new Visible());
		world.Create(new Transform2D(new Vector2(20, 10)), new Sprite(Hosts.Texture()));
		world.Create(new Transform2D(new Vector2(-900, 10)), new Sprite(Hosts.Texture()), new Visible());
		new TransformPropagationSystem(world).Propagate();

		batch.Begin();
		system.Extract(new GameTime());
		batch.End();

		Assert.Equal(new SpriteExtractionStats(2, 0, false), system.LastFrame);
	}
}
