using Kyber.Graphics;

namespace Kyber.Examples.SpriteRenderer;

public class TestSpriteRendererSystem : IInitializeSystem, IUpdateSystem, IRenderSystem
{
	private readonly IWindow _window;
	private readonly ISpriteRenderer _spriteRenderer;
	private readonly ILogger _logger;
	private readonly IInputState _input;
	private readonly IGraphicsDevice _graphicsDevice;
	private readonly Random _rand;

	private readonly List<(Vector2 pos, Vector2 size, Color color, float rotation, float layer)> _blocks = new();

	public bool IsEnabled { get; set; } = true;

	public TestSpriteRendererSystem(IWindow window, ISpriteRenderer spriteRenderer, ILogger<TestSpriteRendererSystem> logger, IInputState input, IGraphicsDevice graphicsDevice)
	{
		_window = window;
		_spriteRenderer = spriteRenderer;
		_logger = logger;
		_input = input;
		_graphicsDevice = graphicsDevice;
		_rand = new Random();
	}

	public void Initialize()
	{
		for (var i = 0; i < 200; i++)
		{
			var size = _rand.Next(80) + 20;
			_blocks.Add((
				new Vector2(_rand.Next(_window.Width - size), _rand.Next(_window.Height - size)),
				new Vector2(size, size),
				new Color(_rand.NextSingle(), _rand.NextSingle(), _rand.NextSingle()),
				MathHelper.TwoPi * _rand.NextSingle(),
				10
			));
		}

		_blocks.Add((new Vector2(125f, 125f), new Vector2(100, 100), Color.Red, 0f, 1));
		_blocks.Add((new Vector2(150f, 150f), new Vector2(100, 100), Color.Yellow, 0f, 2));
		_blocks.Add((new Vector2(100f, 100f), new Vector2(100, 100), Color.Blue, MathHelper.ToRadians(45), 3));
	}

	public void Update(float dt)
	{
		for (int i = 0; i < _blocks.Count; i++)
		{
			var (pos, size, color, rotation, layer) = _blocks[i];

			if (layer < 10) continue;

			var rot = i % 2 == 0 ? -dt : dt;
			_blocks[i] = _blocks[i] with { rotation = (rotation + rot) % MathHelper.TwoPi };
		}
	}

	public void Render(float dt)
	{
		foreach (var (pos, size, color, rotation, layer) in _blocks)
		{
			_spriteRenderer.Draw(color, pos, size, layer: layer, rotation: rotation);
		}
	}
}