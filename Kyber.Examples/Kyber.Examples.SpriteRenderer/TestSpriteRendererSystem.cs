using Kyber.Graphics;

namespace Kyber.Examples.SpriteRenderer;

public class TestSpriteRendererSystem : IInitializeSystem, IUpdateSystem, IRenderSystem
{
	private readonly IWindow _window;
	private readonly ISpriteRenderer _spriteRenderer;
	private readonly ILogger _logger;
	private readonly IInputState _input;
	private readonly Random _rand;

	private readonly List<(Vector2 pos, Vector2 size, Color color, float rotation, byte layer)> _blocks = new();

	public bool IsEnabled { get; set; } = true;

	public TestSpriteRendererSystem(IWindow window, ISpriteRenderer spriteRenderer, ILogger<TestSpriteRendererSystem> logger, IInputState input)
	{
		_window = window;
		_spriteRenderer = spriteRenderer;
		_logger = logger;
		_input = input;
		_rand = new Random();
	}

	public void Initialize()
	{
		//for (var i = 0; i < 2000; i++)
		//{
		//	var size = _rand.Next(80) + 20;
		//	_blocks.Add((
		//		new Vector2(_rand.Next(_window.Width - size), _rand.Next(_window.Height - size)),
		//		new Vector2(size, size),
		//		new Color(_rand.NextSingle(), _rand.NextSingle(), _rand.NextSingle()),
		//		0,
		//		250
		//	));
		//}

		_blocks.Add((new Vector2(125, 125), new Vector2(100, 100), Color.Red, 0, 20));
		_blocks.Add((new Vector2(150, 150), new Vector2(100, 100), Color.Yellow, 0, 50));
		_blocks.Add((new Vector2(100, 100), new Vector2(100, 100), Color.Blue, MathHelper.PiOver2 / 2, 45));

	}

	public void Update(float dt)
	{
		
	}

	public void Render(float dt)
	{
		foreach (var (pos, size, color, rotation, layer) in _blocks)
		{
			_spriteRenderer.Draw(color, pos, size, layer: layer, rotation: rotation, origin: Vector2.One / 2);
		}
	}
}