using System.Runtime.CompilerServices;

namespace Ion;

/// <summary>
/// The engine's frame event buffers. Events emitted in frame N are visible to every listener in frame N (after they are
/// emitted) and in frame N+1; <see cref="Step"/> (run by the event system at the end of the Last stage) moves the current
/// frame's events to the previous frame's buffer and drops the older ones.
/// </summary>
/// <remarks>
/// A frame can run zero fixed steps (when <see cref="GameConfig.MaxFPS"/> is above <see cref="GameConfig.FixedUpdateRate"/>),
/// so the two-frame window alone could hide an event from every FixedUpdate listener. When a game loop is running (an
/// <see cref="ILoopContext"/> is given), an event that leaves the two-frame window before any fixed step has started
/// since it was emitted is kept in a fixed-step backlog, which only FixedUpdate listeners read, until a fixed step has run
/// (at most <see cref="MaxBacklogFrames"/> frames). Listeners in the other stages see exactly the two-frame window.
/// </remarks>
public class EventEmitter : IEventEmitter
{
	/// <summary>
	/// The number of frames an event is kept for FixedUpdate listeners when no fixed step runs, which bounds the backlog
	/// when the loop runs frames without advancing time.
	/// </summary>
	public const int MaxBacklogFrames = 1024;

	private readonly record struct Stamp(long FixedStep, uint Frame);

	private readonly ILoopContext? _loop;

	private RingBuffer<IEvent> _currFrame = new(64);
	private RingBuffer<IEvent> _prevFrame = new(64);
	private RingBuffer<IEvent> _fixedBacklog = new(16);
	private RingBuffer<IEvent> _scratch = new(16);

	private RingBuffer<Stamp> _currStamps = new(64);
	private RingBuffer<Stamp> _prevStamps = new(64);
	private RingBuffer<Stamp> _backlogStamps = new(16);
	private RingBuffer<Stamp> _scratchStamps = new(16);

	private readonly List<EventListener> _listeners = new();
	private uint _nextId = 1;

	/// <summary>
	/// Creates an emitter that is not tied to a game loop: events are visible for exactly two frames.
	/// </summary>
	public EventEmitter() : this(null) { }

	/// <summary>
	/// Creates an emitter that keeps events for FixedUpdate listeners until a fixed step has run (see remarks).
	/// </summary>
	/// <param name="loop">The game loop context, or <see langword="null"/> for the plain two-frame window.</param>
	public EventEmitter(ILoopContext? loop)
	{
		_loop = loop;
	}

	/// <summary>The loop context this emitter uses, if any.</summary>
	internal ILoopContext? Loop => _loop;

	public RingBuffer<IEvent> CurrentFrameEvents => _currFrame;
	public RingBuffer<IEvent> PreviousFrameEvents => _prevFrame;

	/// <summary>
	/// Events older than the previous frame that no fixed step has had a chance to see yet. Read only by listeners
	/// running in FixedUpdate.
	/// </summary>
	public RingBuffer<IEvent> FixedStepBacklog => _fixedBacklog;

	/// <summary>
	/// The id of the oldest event any listener can still see; ids below it are never visible again.
	/// </summary>
	internal ulong OldestLiveEventId =>
		_fixedBacklog.Count > 0 ? _fixedBacklog[0].EventId :
		_prevFrame.Count > 0 ? _prevFrame[0].EventId :
		_currFrame.Count > 0 ? _currFrame[0].EventId :
		(ulong)_nextId + 1;

	/// <summary>
	/// True while the loop is running a FixedUpdate step, when listeners also read <see cref="FixedStepBacklog"/>.
	/// </summary>
	internal bool InFixedStep => _loop is { Stage: GameLoopStage.FixedUpdate };

	public void Step()
	{
		// Rebuild the backlog: keep backlog and previous frame events that no fixed step has started after yet.
		_scratch.Clear();
		_scratchStamps.Clear();

		if (_loop is not null && _loop.Stage != GameLoopStage.None)
		{
			var fixedStep = _loop.FixedStepCount;
			var frame = _loop.Frame;
			_retain(_fixedBacklog, _backlogStamps, fixedStep, frame);
			_retain(_prevFrame, _prevStamps, fixedStep, frame);
		}

		(_fixedBacklog, _scratch) = (_scratch, _fixedBacklog);
		(_backlogStamps, _scratchStamps) = (_scratchStamps, _backlogStamps);

		(_currFrame, _prevFrame) = (_prevFrame, _currFrame);
		(_currStamps, _prevStamps) = (_prevStamps, _currStamps);
		_currFrame.Clear();
		_currStamps.Clear();

		foreach (var listener in _listeners) listener.UpdateKnownEvents();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit<T>() where T : unmanaged
	{
		_currFrame.Add(new Event<T>(Interlocked.Increment(ref _nextId)));
		_currStamps.Add(_stamp());
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit<T>(T data) where T : unmanaged
	{
		_currFrame.Add(new Event<T>(Interlocked.Increment(ref _nextId), data));
		_currStamps.Add(_stamp());
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AttachListener(EventListener listener)
	{
		_listeners.Add(listener);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void DetachListener(EventListener listener)
	{
		_listeners.Remove(listener);
	}

	public IEnumerator<IEvent<T>> GetEvents<T>() where T : unmanaged
	{
		if (InFixedStep)
		{
			for (var i = 0; i < _fixedBacklog.Count; i++)
			{
				if (_fixedBacklog[i].Handled || _fixedBacklog[i] is not IEvent<T>) continue;
				yield return (IEvent<T>)_fixedBacklog[i];
			}
		}

		for (var i = 0; i < _prevFrame.Count; i++)
		{
			if (_prevFrame[i].Handled || _prevFrame[i] is not IEvent<T>) continue;
			yield return (IEvent<T>)_prevFrame[i];
		}

		for (var i = 0; i < _currFrame.Count; i++)
		{
			if (_currFrame[i].Handled || _currFrame[i] is not IEvent<T>) continue;
			yield return (IEvent<T>)_currFrame[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Stamp _stamp() => _loop is null ? default : new Stamp(_loop.FixedStepCount, _loop.Frame);

	private void _retain(RingBuffer<IEvent> events, RingBuffer<Stamp> stamps, long fixedStep, uint frame)
	{
		for (var i = 0; i < events.Count; i++)
		{
			var stamp = stamps[i];

			// A fixed step has started (and, at the end of the frame, finished) since the event was emitted.
			if (stamp.FixedStep != fixedStep) continue;
			if (frame - stamp.Frame >= MaxBacklogFrames) continue;
			if (events[i].Handled) continue;

			_scratch.Add(events[i]);
			_scratchStamps.Add(stamp);
		}
	}
}
