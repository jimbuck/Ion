using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Silk.NET.SDL;

namespace Ion.Extensions.Windowing;

/// <summary>
/// Receives SDL's touch (finger) events and turns them into Ion touch events. Silk.NET.Input 2.x has no touch devices
/// and its SDL input context drops finger events, so this hooks SDL directly with an event watch
/// (<c>SDL_AddEventWatch</c>), which SDL calls for every event as it is queued: on the main thread inside
/// <c>DoEvents()</c> on desktop, and on the Java UI thread on Android. Events are therefore queued under a lock and
/// drained by <see cref="SilkInputState.Step"/>.
/// </summary>
/// <remarks>
/// SDL reports finger positions normalized to the window (0 to 1); they are scaled to the window's size, the space of
/// <see cref="IInputState.MousePosition"/>. SDL finger ids are 64-bit (pointer indices on Android, pointer values on iOS),
/// so each finger that is down gets the lowest free small id (<see cref="TouchIdMap"/>). SDL also synthesizes mouse
/// events from touches by default (<c>SDL_HINT_TOUCH_MOUSE_EVENTS</c>), so games written for the mouse keep working.
/// </remarks>
internal sealed unsafe class SdlTouchSource : IDisposable
{
	private readonly Sdl _sdl;
	private readonly Func<Vector2> _windowSize;
	private readonly Action<InputEvent> _sink;
	private readonly TouchIdMap _ids = new();
	private readonly Lock _lock = new();
	private GCHandle _self;

	/// <summary>Adds the event watch. <paramref name="sink"/> is called under this source's lock, from SDL's event thread.</summary>
	public SdlTouchSource(Sdl sdl, Func<Vector2> windowSize, Action<InputEvent> sink)
	{
		_sdl = sdl;
		_windowSize = windowSize;
		_sink = sink;
		_self = GCHandle.Alloc(this);
		_sdl.AddEventWatch(new PfnEventFilter(&OnEvent), (void*)GCHandle.ToIntPtr(_self));
	}

	/// <summary>Removes the event watch.</summary>
	public void Dispose()
	{
		if (!_self.IsAllocated) return;
		_sdl.DelEventWatch(new PfnEventFilter(&OnEvent), (void*)GCHandle.ToIntPtr(_self));
		_self.Free();
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int OnEvent(void* userdata, Event* e)
	{
		try
		{
			var type = e->Type;
			if (type is not ((uint)EventType.Fingerdown or (uint)EventType.Fingerup or (uint)EventType.Fingermotion)) return 1;
			if (GCHandle.FromIntPtr((nint)userdata).Target is not SdlTouchSource source) return 1;

			var finger = e->Tfinger;
			lock (source._lock)
			{
				if (Map((EventType)type, finger.FingerId, finger.X, finger.Y, source._windowSize(), source._ids) is { } touch) source._sink(touch);
			}
		}
		catch
		{
			// Never let an exception unwind into SDL.
		}

		return 1;
	}

	/// <summary>
	/// Maps one SDL finger event to an Ion touch event, or null when it is not one (or no id is free). Positions are
	/// normalized coordinates scaled by <paramref name="windowSize"/>.
	/// </summary>
	internal static InputEvent? Map(EventType type, long fingerId, float x, float y, Vector2 windowSize, TouchIdMap ids)
	{
		var position = new Vector2(x, y) * windowSize;
		switch (type)
		{
			case EventType.Fingerdown:
				var id = ids.Acquire(fingerId);
				return id < 0 ? null : InputEvent.ForTouch(id, TouchPhase.Began, position);
			case EventType.Fingermotion:
				var moving = ids.Find(fingerId);
				// A motion without a down (the finger was down before the window existed) starts a touch.
				if (moving < 0) moving = ids.Acquire(fingerId);
				return moving < 0 ? null : InputEvent.ForTouch(moving, TouchPhase.Moved, position);
			case EventType.Fingerup:
				var lifted = ids.Release(fingerId);
				return lifted < 0 ? null : InputEvent.ForTouch(lifted, TouchPhase.Ended, position);
			default:
				return null;
		}
	}
}

/// <summary>
/// Gives each 64-bit SDL finger id that is down the lowest free small touch id (0 to <see cref="InputTracker.MaxTouches"/>
/// minus one), and frees it when the finger is lifted.
/// </summary>
internal sealed class TouchIdMap
{
	private readonly long[] _fingers = new long[InputTracker.MaxTouches];
	private readonly bool[] _used = new bool[InputTracker.MaxTouches];

	/// <summary>The number of fingers down.</summary>
	public int Count { get; private set; }

	/// <summary>The touch id of <paramref name="finger"/>, giving it the lowest free one if it has none; -1 when all are taken.</summary>
	public int Acquire(long finger)
	{
		var existing = Find(finger);
		if (existing >= 0) return existing;

		for (var i = 0; i < _used.Length; i++)
		{
			if (_used[i]) continue;
			_used[i] = true;
			_fingers[i] = finger;
			Count++;
			return i;
		}

		return -1;
	}

	/// <summary>The touch id of <paramref name="finger"/>, or -1.</summary>
	public int Find(long finger)
	{
		for (var i = 0; i < _used.Length; i++)
		{
			if (_used[i] && _fingers[i] == finger) return i;
		}

		return -1;
	}

	/// <summary>Frees the touch id of <paramref name="finger"/> and returns it, or -1 when it had none.</summary>
	public int Release(long finger)
	{
		var i = Find(finger);
		if (i < 0) return -1;
		_used[i] = false;
		Count--;
		return i;
	}
}
