namespace Kyber.Scenes.Transitions;

/// <summary>
/// A basic enum to indicate the transition direction.
/// </summary>
public enum TransitionState
{
    /// <summary>
    /// Indicates a transition in.
    /// </summary>
    In,

    /// <summary>
    /// Indicates a transition out.
    /// </summary>
    Out
}

/// <summary>
/// A base Transition used to animate the unload/load of scenes.
/// </summary>
public abstract class Transition : IDisposable
{
    private float _duration;
    private float _halfDuration;
    private float _currentSeconds;

    /// <summary>
    /// The current state of the Transition.
    /// </summary>
    public TransitionState State { get; private set; } = TransitionState.Out;

    /// <summary>
    /// The length of the Transition, in seconds.
    /// </summary>
    public float Duration
    {
        get => _duration;
        internal set
        {
            _duration = value;
            _halfDuration = _duration / 2f;
        }

    }

    /// <summary>
    /// The current relative value of the Transition (0f to 1f).
    /// </summary>
    public float Value => MathHelper.Clamp(_currentSeconds / _halfDuration, 0f, 1f);

    /// <summary>
    /// Triggered when the transition is switching from Out to In.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Triggered when the transition is done.
    /// </summary>
    public event EventHandler? Completed;

    /// <inheritdoc/>
    public abstract void Dispose();

    /// <summary>
    /// Updates the timing of the transition.
    /// </summary>
    /// <param name="gameTime">The elapsed time since the last call.</param>
    public void Update(float dt)
    {
        switch (State)
        {
            case TransitionState.Out:
                _currentSeconds += dt;

                if (_currentSeconds >= _halfDuration)
                {
                    State = TransitionState.In;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
                break;
            case TransitionState.In:
                _currentSeconds -= dt;

                if (_currentSeconds <= 0.0f)
                {
                    Completed?.Invoke(this, EventArgs.Empty);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(State));
        }
    }

    /// <summary>
    /// Renders the transition to the screen.
    /// </summary>
    /// <param name="dt">The elapsed time since the last call.</param>
    public abstract void Render(float dt);
}
