using Kyber.Graphics;

using System.Drawing;

namespace Kyber.Examples.SpriteRenderer;

public class TestSpriteRendererSystem : IInitializeSystem, IUpdateSystem, IRenderSystem
{
	private readonly IWindow _window;
	private readonly ISpriteRenderer _spriteRenderer;
	private readonly ILogger _logger;
	private readonly IInputState _input;
	private readonly IGraphicsDevice _graphicsDevice;
	private readonly Random _rand;

	private readonly Item[] _blocks;

	public bool IsEnabled { get; set; } = true;

	public TestSpriteRendererSystem(IWindow window, ISpriteRenderer spriteRenderer, ILogger<TestSpriteRendererSystem> logger, IInputState input, IGraphicsDevice graphicsDevice)
	{
		_window = window;
		_spriteRenderer = spriteRenderer;
		_logger = logger;
		_input = input;
		_graphicsDevice = graphicsDevice;
		_rand = new Random();

		_blocks = new Item[20_000]; 
	}

	public void Initialize()
	{
		var baseHue = _rand.NextSingle() * 360;
		for (var i = 0; i < _blocks.Length; i++)
		{
			var size = _rand.Next(80) + 20;
			var hue = baseHue + (6f * i / _blocks.Length);
			_blocks[i] = new Item()
			{
				Rect = new(_rand.Next(_window.Width - size), _rand.Next(_window.Height - size), size, size),
				Color = Color.FromHSV(hue, 1f, 1f),
				Hue = hue,
				//Rotation = MathHelper.TwoPi * _rand.NextSingle(),
				//Layer = 10
				Velocity = new((_rand.NextSingle() * 70) + 100, (_rand.NextSingle() * 70) + 100)
			};
		}
	}

	public void Update(float dt)
	{
		for (int i = 0; i < _blocks.Length; i++)
		{
			_blocks[i].Rect.X += (_blocks[i].Velocity.X * dt);
			_blocks[i].Rect.Y += (_blocks[i].Velocity.Y * dt);

			if (_blocks[i].Rect.Left < 0)
			{
				_blocks[i].Rect.X = 0;
				_blocks[i].Velocity.X *= -1f;
				_blocks[i].Hue += 6;
				_blocks[i].Color = Color.FromHSV(_blocks[i].Hue, 1f, 1f);
			}
			else if (_blocks[i].Rect.Right > _window.Width)
			{
				_blocks[i].Rect.X = _window.Width - _blocks[i].Rect.Width;
				_blocks[i].Velocity.X *= -1f;
				_blocks[i].Hue += 6;
				_blocks[i].Color = Color.FromHSV(_blocks[i].Hue, 1f, 1f);
			}
			
			if (_blocks[i].Rect.Top < 0)
			{
				_blocks[i].Rect.Y = 0;
				_blocks[i].Velocity.Y *= -1f;
				_blocks[i].Hue += 6;
				_blocks[i].Color = Color.FromHSV(_blocks[i].Hue, 1f, 1f);
			}
			else if (_blocks[i].Rect.Bottom > _window.Height)
			{
				_blocks[i].Rect.Y = _window.Height - _blocks[i].Rect.Height;
				_blocks[i].Velocity.Y *= -1f;
				_blocks[i].Hue += 6;
				_blocks[i].Color = Color.FromHSV(_blocks[i].Hue, 1f, 1f);
			}
		}
	}

	public void Render(float dt)
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