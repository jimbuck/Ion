namespace Kyber.Examples.SpriteRenderer;

public class UserInputSystem : IUpdateSystem
{
	private readonly IInputState _inputState;
	private readonly IEventEmitter _eventEmitter;

	public bool IsEnabled { get; set; } = true;

	public UserInputSystem(IInputState inputState, IEventEmitter eventEmitter)
	{
		_inputState = inputState;
		_eventEmitter = eventEmitter;
	}

	public void Update(GameTime dt)
	{
		if (_inputState.Pressed(Key.Escape)) _eventEmitter.Emit<GameExitEvent>();
	}
}
