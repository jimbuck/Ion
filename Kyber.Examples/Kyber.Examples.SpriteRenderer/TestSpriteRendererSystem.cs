using Kyber.Assets;
using Kyber.Graphics;

using System.Drawing;

namespace Kyber.Examples.SpriteRenderer;

public class TestSpriteRendererSystem : IInitializeSystem, IUpdateSystem, IRenderSystem
{
	private readonly Vector2 VECTOR2_HALF = Vector2.One / 2f;

	private readonly IWindow _window;
	private readonly ISpriteRenderer _spriteRenderer;
	private readonly ILogger _logger;
	private readonly IInputState _input;
	private readonly IGraphicsDevice _graphicsDevice;
	private readonly IAssetManager _assetManager;
	private readonly Random _rand;

	private readonly Item[] _bouncingSprites;
	private const float _saturation = 0.5f;
	private const float _value = 1f;
	private Texture2D? _texture;

	private readonly Item[] _depthBlocks;

	public bool IsEnabled { get; set; } = true;

	public TestSpriteRendererSystem(
		IWindow window, ISpriteRenderer spriteRenderer, ILogger<TestSpriteRendererSystem> logger, 
		IInputState input, IGraphicsDevice graphicsDevice, IAssetManager assetManager)
	{
		_window = window;
		_spriteRenderer = spriteRenderer;
		_logger = logger;
		_input = input;
		_graphicsDevice = graphicsDevice;
		_assetManager = assetManager;
		_rand = new Random();

		_bouncingSprites = new Item[1_000];
		_depthBlocks = new Item[8];
	}

	public void Initialize()
	{
		_texture = _assetManager.Load<Texture2D>("Tile.png");

		var depthBlockSize = 100;
		var center = (_window.Size / 2) - new Vector2(depthBlockSize/2);
		var radius = 50;

		for (var i = 0; i < _depthBlocks.Length; i++)
		{
			var x = center.X + (radius * MathF.Cos(i * MathHelper.TwoPi / _depthBlocks.Length));
			var y = center.Y + (radius * MathF.Sin(i * MathHelper.TwoPi / _depthBlocks.Length));
			var hue = i * 256 / _depthBlocks.Length;
			_depthBlocks[i] = new Item()
			{
				Rect = new(x, y, depthBlockSize, depthBlockSize),
				Color = Color.FromHSV(hue, _saturation, _value),
				Hue = hue,
				Rotation = 0,
				Layer = 10 + (float)(_rand.NextDouble() * 64),
			};
		}

		var baseHue = _rand.NextSingle() * 360;
		for (var i = 0; i < _bouncingSprites.Length; i++)
		{
			var size = _rand.Next(80) + 20;
			var hue = baseHue + (6f * i / _bouncingSprites.Length);
			var angle = _rand.NextSingle() * MathHelper.TwoPi;
			var speed = _rand.NextSingle() * 200 + 50;

			_bouncingSprites[i] = new Item()
			{
				Rect = new(_rand.Next(_window.Width - size), _rand.Next(_window.Height - size), size, size),
				Color = Color.FromHSV(hue, _saturation, _value),
				Hue = hue,
				Rotation = 0,//MathHelper.TwoPi * _rand.NextSingle(),
				Layer = 10 + (float)(_rand.NextDouble() * 64),
				Velocity = new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * speed
			};
		}
	}

	public void Update(GameTime dt)
	{
		_updateBouncingSprites(dt);
	}

	public void Render(GameTime dt)
	{
		_renderDepthTest(dt);
		_renderBouncingSprites(dt);
	}

	private void _renderDepthTest(GameTime dt)
	{
		if (_texture is null) return;

		var i = 0;
		foreach (var block in _depthBlocks)
		{
			if (i++ % 2 == 1)
			_spriteRenderer.Draw(_texture, block.Rect, color: block.Color, depth: block.Layer, rotation: block.Rotation);
			else
				_spriteRenderer.DrawRect(color: block.Color, block.Rect, depth: block.Layer, rotation: block.Rotation);
		}
	}

	private void _updateBouncingSprites(GameTime dt)
	{
		var minX = 0;// -_window.Width;
		var minY = 0;// -_window.Height;
		var maxX = _window.Width;
		var maxY = _window.Height;

		for (int i = 0; i < _bouncingSprites.Length; i++)
		{
			_bouncingSprites[i].Rect.X += (_bouncingSprites[i].Velocity.X * dt);
			_bouncingSprites[i].Rect.Y += (_bouncingSprites[i].Velocity.Y * dt);
			//_blocks[i].Rotation += (MathHelper.TwoPi * 0.1f * dt);

			if (_bouncingSprites[i].Rect.Left < minX)
			{
				_bouncingSprites[i].Rect.X = minX;
				_bouncingSprites[i].Velocity.X *= -1f;
				_bouncingSprites[i].Hue += 6;
				_bouncingSprites[i].Color = Color.FromHSV(_bouncingSprites[i].Hue, _saturation, _value);
			}
			else if (_bouncingSprites[i].Rect.Right > maxX)
			{
				_bouncingSprites[i].Rect.X = maxX - _bouncingSprites[i].Rect.Width;
				_bouncingSprites[i].Velocity.X *= -1f;
				_bouncingSprites[i].Hue += 6;
				_bouncingSprites[i].Color = Color.FromHSV(_bouncingSprites[i].Hue, _saturation, _value);
			}

			if (_bouncingSprites[i].Rect.Top < minY)
			{
				_bouncingSprites[i].Rect.Y = minY;
				_bouncingSprites[i].Velocity.Y *= -1f;
				_bouncingSprites[i].Hue += 6;
				_bouncingSprites[i].Color = Color.FromHSV(_bouncingSprites[i].Hue, _saturation, _value);
			}
			else if (_bouncingSprites[i].Rect.Bottom > maxY)
			{
				_bouncingSprites[i].Rect.Y = maxY - _bouncingSprites[i].Rect.Height;
				_bouncingSprites[i].Velocity.Y *= -1f;
				_bouncingSprites[i].Hue += 6;
				_bouncingSprites[i].Color = Color.FromHSV(_bouncingSprites[i].Hue, _saturation, _value);
			}
		}
	}

	private void _renderBouncingSprites(GameTime dt)
	{
		if (_texture is null) return;

		var i = 0;
		foreach (var block in _bouncingSprites)
		{
			if (i++ % 2 == 1)
				_spriteRenderer.Draw(_texture, block.Rect, color: block.Color, depth: block.Layer, rotation: block.Rotation);
			else
				_spriteRenderer.DrawRect(color: block.Color, block.Rect, depth: block.Layer, rotation: block.Rotation);
		}
	}

	private struct Item
	{
		public RectangleF Rect;
		public Color Color;
		public float Rotation;
		public float Layer;
		public Vector2 Velocity;
		public float Hue;
	}
}