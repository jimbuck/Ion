using Kyber.Graphics;

using Veldrid;

namespace Kyber.Examples.SpriteRenderer;

public class TestSpriteRendererSystem : IInitializeSystem, IUpdateSystem, IRenderSystem
{
	private readonly IWindow _window;
	private readonly ISpriteRenderer _spriteRenderer;
	private readonly ILogger _logger;
	private readonly IInputState _input;
	private readonly IGraphicsDevice _graphicsDevice;
	private readonly Random _rand;

	private Vector2 _lastMousePosition = Vector2.Zero;

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
		for (var i = 0; i < 2000; i++)
		{
			var size = _rand.Next(80) + 20;
			_blocks.Add((
				new Vector2(_rand.Next(_window.Width - size), _rand.Next(_window.Height - size)),
				new Vector2(size, size),
				new Color(_rand.NextSingle(), _rand.NextSingle(), _rand.NextSingle()),
				0,
				(float)i
			));
		}

		_blocks.Add((new Vector2(125f, 125f), new Vector2(100, 100), Color.Red, 0, 1f));
		_blocks.Add((new Vector2(150f, 150f), new Vector2(100, 100), Color.Yellow, 0, 2f));
		_blocks.Add((new Vector2(100f, 100f), new Vector2(100, 100), Color.Blue, MathHelper.ToRadians(45), 3f));
	}

	public void Update(float dt)
	{
		if (_lastMousePosition == _input.MousePosition) return;

		var clipSpace = Vector3.Transform(new(_input.MousePosition, _input.MousePosition.X), _graphicsDevice.ProjectionMatrix);

		Console.WriteLine($"Clip Space: {clipSpace}");

		_lastMousePosition = _input.MousePosition;
	}

	public void Render(float dt)
	{
		foreach (var (pos, size, color, rotation, layer) in _blocks)
		{
			_spriteRenderer.Draw(color, pos, size, layer: layer, rotation: rotation);
		}
	}
}