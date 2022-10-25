namespace Kyber;

internal class ExitSystem : IPostUpdateSystem
{
	private readonly Game _game;
	private readonly IEventListener _events;

	public bool IsEnabled { get; set; } = true;

	public ExitSystem(Game game, IEventListener events)
	{
		_game = game;
		_events = events;
	}

	public void PostUpdate(float dt)
	{
		if (_events.On<WindowClosedEvent>() || _events.On<GameExitEvent>()) _game.Exit();
	}
}