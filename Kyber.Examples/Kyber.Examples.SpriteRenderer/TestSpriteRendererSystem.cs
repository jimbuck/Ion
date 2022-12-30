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
	private readonly Random _rand;

	private readonly Item[] _blocks;
	private const float _saturation = 0.5f;
	private const float _value = 1f;

	public bool IsEnabled { get; set; } = true;

	public TestSpriteRendererSystem(IWindow window, ISpriteRenderer spriteRenderer, ILogger<TestSpriteRendererSystem> logger, IInputState input, IGraphicsDevice graphicsDevice)
	{
		_window = window;
		_spriteRenderer = spriteRenderer;
		_logger = logger;
		_input = input;
		_graphicsDevice = graphicsDevice;
		_rand = new Random();

		_blocks = new Item[10_000];
	}

	public void Initialize()
	{
		var baseHue = _rand.NextSingle() * 360;
		for (var i = 0; i < _blocks.Length; i++)
		{
			var size = _rand.Next(80) + 20;
			var hue = baseHue + (6f * i / _blocks.Length);
			var angle = _rand.NextSingle() * MathHelper.TwoPi;
			var speed = _rand.NextSingle() * 200 + 50;			

			_blocks[i] = new Item()
			{
				Rect = new(_rand.Next(_window.Width - size), _rand.Next(_window.Height - size), size, size),
				Color = Color.FromHSV(hue, _saturation, _value),
				Hue = hue,
				Rotation = 0,//MathHelper.TwoPi * _rand.NextSingle(),
				Layer = 10,
				Velocity = new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * speed
			};
		}
	}

	public void Update(GameTime dt)
	{
		var minX = 0;// -_window.Width;
		var minY = 0;// -_window.Height;
		var maxX = _window.Width;
		var maxY = _window.Height;

		for (int i = 0; i < _blocks.Length; i++)
		{
			_blocks[i].Rect.X += (_blocks[i].Velocity.X * dt);
			_blocks[i].Rect.Y += (_blocks[i].Velocity.Y * dt);
			//_blocks[i].Rotation += (MathHelper.TwoPi * 0.1f * dt);

			if (_blocks[i].Rect.Left < minX)
			{
				_blocks[i].Rect.X = minX;
				_blocks[i].Velocity.X *= -1f;
				_blocks[i].Hue += 6;
				_blocks[i].Color = Color.FromHSV(_blocks[i].Hue, _saturation, _value);
			}
			else if (_blocks[i].Rect.Right > maxX)
			{
				_blocks[i].Rect.X = maxX - _blocks[i].Rect.Width;
				_blocks[i].Velocity.X *= -1f;
				_blocks[i].Hue += 6;
				_blocks[i].Color = Color.FromHSV(_blocks[i].Hue, _saturation, _value);
			}
			
			if (_blocks[i].Rect.Top < minY)
			{
				_blocks[i].Rect.Y = minY;
				_blocks[i].Velocity.Y *= -1f;
				_blocks[i].Hue += 6;
				_blocks[i].Color = Color.FromHSV(_blocks[i].Hue, _saturation, _value);
			}
			else if (_blocks[i].Rect.Bottom > maxY)
			{
				_blocks[i].Rect.Y = maxY - _blocks[i].Rect.Height;
				_blocks[i].Velocity.Y *= -1f;
				_blocks[i].Hue += 6;
				_blocks[i].Color = Color.FromHSV(_blocks[i].Hue, _saturation, _value);
			}
		}
	}

	public void Render(GameTime dt)
	{
		foreach (var block in _blocks)
		{
			_spriteRenderer.DrawRect(block.Color, block.Rect, depth: block.Layer, rotation: block.Rotation);
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