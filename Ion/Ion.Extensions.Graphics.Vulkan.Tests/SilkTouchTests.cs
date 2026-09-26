using System.Numerics;

using Microsoft.Extensions.Configuration;

using Ion.Extensions.Windowing;

using Silk.NET.SDL;

namespace Ion.Extensions.Graphics.Vulkan.Tests;

/// <summary>
/// The Silk.NET module's mapping of SDL finger events to Input v2 touches, without a window or a touch screen.
/// </summary>
public class SilkTouchTests
{
	private static readonly Vector2 WindowSize = new(640, 480);

	[Fact, Trait(CATEGORY, UNIT)]
	public void FingerEventsMapToTouchEventsInWindowCoordinates()
	{
		var ids = new TouchIdMap();

		var down = SdlTouchSource.Map(EventType.Fingerdown, 0x7fff_1234_5678, 0.5f, 0.25f, WindowSize, ids);
		Assert.Equal(InputEvent.ForTouch(0, TouchPhase.Began, new Vector2(320, 120)), down);

		var motion = SdlTouchSource.Map(EventType.Fingermotion, 0x7fff_1234_5678, 1f, 1f, WindowSize, ids);
		Assert.Equal(InputEvent.ForTouch(0, TouchPhase.Moved, new Vector2(640, 480)), motion);

		var up = SdlTouchSource.Map(EventType.Fingerup, 0x7fff_1234_5678, 1f, 1f, WindowSize, ids);
		Assert.Equal(InputEvent.ForTouch(0, TouchPhase.Ended, new Vector2(640, 480)), up);
		Assert.Equal(0, ids.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LargeFingerIdsGetTheLowestFreeSmallIds()
	{
		var ids = new TouchIdMap();
		Assert.Equal(0, ids.Acquire(long.MaxValue));
		Assert.Equal(1, ids.Acquire(-5));
		Assert.Equal(2, ids.Acquire(12345678901));
		Assert.Equal(1, ids.Acquire(-5));

		Assert.Equal(1, ids.Release(-5));
		Assert.Equal(-1, ids.Release(-5));
		Assert.Equal(1, ids.Acquire(99));
		Assert.Equal(3, ids.Count);

		for (var i = 0; i < InputTracker.MaxTouches - 3; i++) Assert.True(ids.Acquire(1000 + i) >= 0);
		Assert.Equal(-1, ids.Acquire(5000));
		Assert.Null(SdlTouchSource.Map(EventType.Fingerdown, 5000, 0, 0, WindowSize, ids));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MotionWithoutDownStartsATouchAndUpWithoutDownIsDropped()
	{
		var ids = new TouchIdMap();
		Assert.Null(SdlTouchSource.Map(EventType.Fingerup, 7, 0, 0, WindowSize, ids));
		Assert.Equal(InputEvent.ForTouch(0, TouchPhase.Moved, new Vector2(64, 48)), SdlTouchSource.Map(EventType.Fingermotion, 7, 0.1f, 0.1f, WindowSize, ids));
		Assert.Null(SdlTouchSource.Map(EventType.Mousemotion, 7, 0, 0, WindowSize, ids));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void QueuedTouchesReachTheTrackerAtTheNextStep()
	{
		var config = new ConfigurationBuilder().Build();
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IEvents>(new EventBus());
		services.AddSilkWindowing(config);
		var provider = services.BuildServiceProvider();
		var input = provider.GetRequiredService<SilkInputState>();
		var ids = new TouchIdMap();

		input.EnqueueTouch(SdlTouchSource.Map(EventType.Fingerdown, 42, 0.5f, 0.5f, WindowSize, ids)!.Value);
		input.EnqueueTouch(SdlTouchSource.Map(EventType.Fingerdown, 43, 0.25f, 0.5f, WindowSize, ids)!.Value);
		Assert.True(((IInputState)input).Touches.IsEmpty);

		input.Step();
		var touches = input.Touches.ToArray();
		Assert.Equal(2, touches.Length);
		Assert.Equal(new TouchPoint(0, new Vector2(320, 240), Vector2.Zero, new Vector2(320, 240), TouchPhase.Began, true), touches[0]);
		Assert.Equal(1, touches[1].Id);

		input.EnqueueTouch(SdlTouchSource.Map(EventType.Fingerup, 42, 0.5f, 0.5f, WindowSize, ids)!.Value);
		input.Step();
		Assert.True(input.Touches[0].Released);
		input.Step();
		Assert.Equal(1, Assert.Single(input.Touches.ToArray()).Id);
	}
}
