using Arch.Core;

using Ion.Assets;
using Ion.Graphics;
using Ion.Utils;

namespace Ion.Examples.SpriteRenderer;

struct HueComponent
{
	public float Hue;

	public HueComponent(float hue)
	{
		Hue = hue;	
	}
}
record struct TestBlockComponent(bool Moving);

public class TestSpriteRendererSystem : IInitializeSystem, IUpdateSystem, IRenderSystem
{
	private readonly Vector2 VECTOR2_HALF = Vector2.One / 2f;

	private readonly IWindow _window;
	private readonly ISpriteBatch _spriteRenderer;
	private readonly ILogger _logger;
	private readonly IInputState _input;
	private readonly IAssetManager _assetManager;
	private readonly World _world;
	private readonly Random _rand;

	private readonly int _bouncingSpriteCount;
	private readonly QueryDescription _bouncingSpritesQuery;

	private const float _saturation = 0.5f;
	private const float _value = 1f;
	private Texture2D? _texture;

	private readonly int _depthBlockCount;
	private readonly QueryDescription _spriteQuery;

	public bool IsEnabled { get; set; } = true;

	public TestSpriteRendererSystem(
		IWindow window, ISpriteBatch spriteRenderer, ILogger<TestSpriteRendererSystem> logger, 
		IInputState input, IAssetManager assetManager,
		World world)
	{
		_window = window;
		_spriteRenderer = spriteRenderer;
		_logger = logger;
		_input = input;
		_assetManager = assetManager;
		_world = world;
		_rand = new Random();

		_bouncingSpriteCount = 50_000;

		_depthBlockCount = 8;
		_spriteQuery = new QueryDescription().WithAll<TestBlockComponent>();
	}

	public void Initialize()
	{
		using var _ = MicroTimer.Start("TestSpriteRendererSystem::Initialize");

		_texture = _assetManager.Load<Texture2D>("Tile.png");

		var depthBlockSize = 100;
		var center = (_window.Size / 2) - new Vector2(depthBlockSize/2);
		var radius = 50;

		for (var i = 0; i < _depthBlockCount; i++)
		{
			var x = center.X + (radius * MathF.Cos(i * MathHelper.TwoPi / _depthBlockCount));
			var y = center.Y + (radius * MathF.Sin(i * MathHelper.TwoPi / _depthBlockCount));
			var hue = i * 256 / _depthBlockCount;

			_world.Create(
				new TextureComponent(_texture),
				new Position2dComponent(new RectangleF(x, y, depthBlockSize, depthBlockSize), 10 + (float)(_rand.NextDouble() * 64)),
				new ColorComponent(Color.FromHSV(hue, _saturation, _value)),
				new HueComponent(hue),
				new TestBlockComponent(false)
			);
		}

		var baseHue = _rand.NextSingle() * 360;
		for (var i = 0; i < _bouncingSpriteCount; i++)
		{
			var size = _rand.Next(80) + 20;
			var hue = baseHue + (6f * i / _bouncingSpriteCount);
			var angle = _rand.NextSingle() * MathHelper.TwoPi;
			var speed = _rand.NextSingle() * 200 + 50;

			_world.Create(
				new TextureComponent(_texture),
				new Position2dComponent(new RectangleF(_rand.Next(_window.Width - size), _rand.Next(_window.Height - size), size, size), 10 + (float)(_rand.NextDouble() * 64)),
				new ColorComponent(Color.FromHSV(hue, _saturation, _value)),
				new HueComponent(hue),
				new TestBlockComponent(true),
				new Velocity2dComponent(new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * speed)
			);
		}
	}

	public void Update(GameTime dt)
	{
		using var _ = MicroTimer.Start(name: "TestSpriteRenderSystem::Update");

		var minX = 0;
		var minY = 0;
		var maxX = _window.Width;
		var maxY = _window.Height;

		_world.Query(in _spriteQuery, (ref TestBlockComponent block, ref Position2dComponent pos, ref ColorComponent color, ref HueComponent hue, ref Velocity2dComponent vel) =>
		{
			if (!block.Moving) return;

			pos.Position.X += (vel.Velocity.X * dt);
			pos.Position.Y += (vel.Velocity.Y * dt);

			if (pos.Position.Left < minX)
			{
				pos.Position.X = minX;
				vel.Velocity.X *= -1f;
				hue.Hue += 6;
				color.Color = Color.FromHSV(hue.Hue, _saturation, _value);
			}
			else if (pos.Position.Right > maxX)
			{
				pos.Position.X = maxX - pos.Position.Width;
				vel.Velocity.X *= -1f;
				hue.Hue += 6;
				color.Color = Color.FromHSV(hue.Hue, _saturation, _value);
			}

			if (pos.Position.Top < minY)
			{
				pos.Position.Y = minY;
				vel.Velocity.Y *= -1f;
				hue.Hue += 6;
				color.Color = Color.FromHSV(hue.Hue, _saturation, _value);
			}
			else if (pos.Position.Bottom > maxY)
			{
				pos.Position.Y = maxY - pos.Position.Height;
				vel.Velocity.Y *= -1f;
				hue.Hue += 6;
				color.Color = Color.FromHSV(hue.Hue, _saturation, _value);
			}
		});
	}

	public void Render(GameTime dt)
	{
		using var _ = MicroTimer.Start(name: "TestSpriteRenderSystem::Render");

		var i = 0;
		_world.Query(in _spriteQuery, (ref TextureComponent tex, ref Position2dComponent pos, ref Rotation2dComponent rot, ref ColorComponent color) =>
		{
			//var texture = _assetManager.Get<Texture2D>(tex.TextureId);

			if (i++ % 2 == 1)
				_spriteRenderer.Draw(_texture!, pos.Position, color: color.Color, depth: pos.Depth, rotation: rot.Angle);
			else
				_spriteRenderer.DrawRect(color: color.Color, pos.Position, depth: pos.Depth, rotation: rot.Angle);
		});
	}
}