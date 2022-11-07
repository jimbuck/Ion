namespace Kyber.Examples.Simple;

public class SceneSwitcherSystem : IUpdateSystem
{
	private readonly ILogger _logger;
	private readonly SceneManager _sceneManager;
    private readonly float _max = 1;

	private float _countdown = 0;
	private int _index = 0;

	public bool IsEnabled { get; set; } = true;

	public SceneSwitcherSystem(ILogger<SceneSwitcherSystem> logger, SceneManager sceneManager)
	{
		_logger = logger;
		_sceneManager = sceneManager;
		_countdown = _max;
	}

	public void Update(GameTime dt)
	{
		_countdown -= dt;

		if (_countdown <= 0 )
		{
			_countdown = _max;
			_index = (_index + 1) % _sceneManager.Scenes.Length;
            _logger.LogDebug("Changing scenes ({0} = {1})!", _index, _sceneManager.Scenes[_index]);
            _sceneManager.LoadScene(_sceneManager.Scenes[_index]);
		}
	}
}
