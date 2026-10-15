using System.Numerics;

namespace Ion;

/// <summary>
/// Supplies injected input events to an <see cref="InputTracker"/> at the start of every frame (set as
/// <see cref="InputTracker.Script"/>). Unlike <see cref="IInputPlayback"/> it does not replace device input.
/// </summary>
public interface IInputScript
{
	/// <summary>Applies the events due at <paramref name="frame"/> to <paramref name="sink"/>. Called on the game thread.</summary>
	void Apply(uint frame, IInputEventSink sink);
}

/// <summary>
/// A thread-safe queue of input events injected by tools and agents (the remote protocol's <c>input.send</c>), applied to
/// the application's <see cref="InputTracker"/> at the start of a frame, as if the device had produced them. Events can be
/// delayed by a number of frames, so a key can be held for a while and released later. Register it with
/// <see cref="InputServiceCollectionExtensions.AddScriptedInput"/>.
/// </summary>
/// <remarks>
/// Injected events go through the tracker's normal path: edges, fixed-step views and <see cref="InputTracker.Recorder"/>
/// all see them, so a session driven by an agent can be recorded and replayed with <see cref="InputPlayer"/>. While an
/// <see cref="InputPlayer"/> is playing, the tracker ignores them (the recording owns the input).
/// </remarks>
public sealed class ScriptedInput : IInputScript, IInputTrackerHook
{
	private readonly Lock _lock = new();
	private readonly List<(long Due, InputEvent Event)> _pending = [];
	private readonly List<InputEvent> _due = [];
	private long _frames;

	/// <summary>The number of events applied so far.</summary>
	public long AppliedCount { get; private set; }

	/// <summary>The number of events waiting to be applied.</summary>
	public int PendingCount
	{
		get
		{
			lock (_lock) return _pending.Count;
		}
	}

	/// <inheritdoc/>
	public void Attach(InputTracker tracker)
	{
		ArgumentNullException.ThrowIfNull(tracker);
		tracker.Script = this;
	}

	/// <summary>
	/// Queues <paramref name="e"/> for the start of the next frame, or <paramref name="delayFrames"/> frames after it.
	/// Safe to call from any thread. Events due in the same frame are applied in the order they were queued.
	/// </summary>
	public void Enqueue(in InputEvent e, int delayFrames = 0)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(delayFrames);
		lock (_lock) _pending.Add((_frames + delayFrames, e));
	}

	/// <summary>Queues a key press and release within the next frame (both edges are seen that frame).</summary>
	public void Tap(Key key, ModifierKeys modifiers = ModifierKeys.None)
	{
		Enqueue(InputEvent.ForKey(key, true, modifiers: modifiers));
		Enqueue(InputEvent.ForKey(key, false, modifiers: modifiers));
	}

	/// <summary>Queues a key press now and its release <paramref name="frames"/> frames later (at least one).</summary>
	public void Hold(Key key, int frames, ModifierKeys modifiers = ModifierKeys.None)
	{
		Enqueue(InputEvent.ForKey(key, true, modifiers: modifiers));
		Enqueue(InputEvent.ForKey(key, false, modifiers: modifiers), Math.Max(1, frames));
	}

	/// <summary>Queues a pointer move to <paramref name="position"/> and a click (press and release) of <paramref name="button"/> there.</summary>
	public void Click(Vector2 position, MouseButton button = MouseButton.Left)
	{
		Enqueue(InputEvent.ForMouseMove(position));
		Enqueue(InputEvent.ForMouseButton(button, true));
		Enqueue(InputEvent.ForMouseButton(button, false));
	}

	/// <summary>Queues every character of <paramref name="text"/> as text input.</summary>
	public void Type(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		foreach (var c in text) Enqueue(InputEvent.ForText(c));
	}

	/// <summary>Drops every pending event.</summary>
	public void Clear()
	{
		lock (_lock) _pending.Clear();
	}

	/// <inheritdoc/>
	public void Apply(uint frame, IInputEventSink sink)
	{
		ArgumentNullException.ThrowIfNull(sink);
		_due.Clear();
		lock (_lock)
		{
			var now = _frames++;
			var kept = 0;
			for (var i = 0; i < _pending.Count; i++)
			{
				var item = _pending[i];
				if (item.Due <= now) _due.Add(item.Event);
				else _pending[kept++] = item;
			}

			_pending.RemoveRange(kept, _pending.Count - kept);
		}

		foreach (var e in _due) e.ApplyTo(sink);
		AppliedCount += _due.Count;
	}
}
