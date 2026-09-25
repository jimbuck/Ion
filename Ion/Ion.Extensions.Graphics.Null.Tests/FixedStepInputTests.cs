using System.Numerics;

namespace Ion.Extensions.Graphics.Tests;

/// <summary>
/// Input edges and deltas must reach FixedUpdate systems exactly once however many frames run between fixed steps. At
/// 120 fps and 60 Hz about every other frame runs no fixed step.
/// </summary>
public class FixedStepInputTests
{
	private static IonApplication Create(out ManualClock clock, out InputProbe probe)
	{
		var manual = new ManualClock();
		var app = TestApp.CreateHeadless(
			services => services
				.AddSingleton<IClock>(manual)
				.Configure<GameConfig>(c => { c.MaxFPS = 120; c.FixedUpdateRate = 60; })
				.AddSingleton<InputProbe>(),
			use => use.UseSystem<InputProbe>());

		clock = manual;
		probe = app.Services.GetRequiredService<InputProbe>();
		return app;
	}

	[Theory, Trait(CATEGORY, INTEGRATION)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	[InlineData(6)]
	[InlineData(7)]
	[InlineData(8)]
	public void ClickOnAnyFrameIsSeenOnceInFixedUpdateAndOnceInUpdate(int clickFrame)
	{
		using var app = Create(out _, out var probe);
		var input = app.Services.GetRequiredService<NullInputState>();
		var loop = app.Build();

		for (var frame = 0; frame < 24; frame++)
		{
			if (frame == clickFrame) input.Click(MouseButton.Left);
			loop.Step();
		}

		Assert.Equal(1, probe.FixedPressed);
		Assert.Equal(1, probe.FixedReleased);
		Assert.Equal(1, probe.UpdatePressed);
		Assert.Equal(1, probe.UpdateReleased);
		Assert.Contains(0, probe.FixedStepsPerFrame);
	}

	[Theory, Trait(CATEGORY, INTEGRATION)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public void KeyTapOnAnyFrameIsSeenOnceInFixedUpdateWithItsModifiers(int tapFrame)
	{
		using var app = Create(out _, out var probe);
		var input = app.Services.GetRequiredService<NullInputState>();
		var loop = app.Build();

		for (var frame = 0; frame < 12; frame++)
		{
			if (frame == tapFrame) input.Tap(Key.Space, ModifierKeys.Shift);
			loop.Step();
		}

		Assert.Equal(1, probe.FixedSpacePressed);
		Assert.Equal(1, probe.FixedShiftSpacePressed);
		Assert.Equal(1, probe.UpdateSpacePressed);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void KeyHeldAcrossFramesStaysDownInBothViewsAndEdgesAreSeenOnce()
	{
		using var app = Create(out _, out var probe);
		var input = app.Services.GetRequiredService<NullInputState>();
		var loop = app.Build();

		for (var frame = 0; frame < 30; frame++)
		{
			if (frame == 3) input.Press(Key.W);
			if (frame == 17) input.Release(Key.W);
			loop.Step();
		}

		Assert.Equal(1, probe.FixedWPressed);
		Assert.Equal(1, probe.FixedWReleased);
		Assert.Equal(1, probe.UpdateWPressed);
		Assert.Equal(1, probe.UpdateWReleased);

		// Frames 3 to 16 (applied at the start of frames 3 and 17) hold the key in every stage.
		Assert.Equal(14, probe.UpdateWDownFrames);
		Assert.True(probe.FixedWDownSteps >= 6, $"W was down in {probe.FixedWDownSteps} fixed steps");
		Assert.Equal(probe.FixedWDownSteps, probe.FixedStepsWhileWHeld);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void WheelAndMouseDeltasAccumulateAcrossFramesForFixedSteps()
	{
		using var app = Create(out _, out var probe);
		var input = app.Services.GetRequiredService<NullInputState>();
		var loop = app.Build();

		for (var frame = 0; frame < 20; frame++)
		{
			if (frame >= 1 && frame <= 10)
			{
				input.Scroll(1f);
				input.SetMousePosition(new Vector2(frame * 10, frame * 5));
			}

			loop.Step();
		}

		// Every frame's movement is reported to exactly one fixed step, and to exactly one frame.
		Assert.Equal(10f, probe.FixedWheelTotal);
		Assert.Equal(10f, probe.UpdateWheelTotal);
		Assert.Equal(new Vector2(100, 50), probe.FixedMouseTotal);
		Assert.Equal(new Vector2(100, 50), probe.UpdateMouseTotal);

		// Fixed steps that follow a frame without a fixed step see two frames' worth of movement.
		Assert.Contains(2f, probe.FixedWheelDeltas);
		Assert.All(probe.UpdateWheelDeltas, d => Assert.InRange(d, 0f, 1f));
		Assert.Equal(new Vector2(100, 50), input.MousePosition);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WithoutALoopEveryQueryUsesTheFrameView()
	{
		var input = new NullInputState();

		input.Click();
		input.Scroll(2);
		input.SetMousePosition(3, 4);
		input.Step();

		Assert.True(input.Pressed(MouseButton.Left));
		Assert.Equal(2f, input.WheelDelta);
		Assert.Equal(new Vector2(3, 4), input.MouseDelta);

		input.Step();
		Assert.False(input.Pressed(MouseButton.Left));
		Assert.Equal(0f, input.WheelDelta);
		Assert.Equal(Vector2.Zero, input.MouseDelta);
		Assert.Equal(new Vector2(3, 4), input.MousePosition);
	}

	public sealed class InputProbe(IInputState input)
	{
		private int _stepsThisFrame;

		public int FixedPressed, FixedReleased, UpdatePressed, UpdateReleased;
		public int FixedSpacePressed, FixedShiftSpacePressed, UpdateSpacePressed;
		public int FixedWPressed, FixedWReleased, UpdateWPressed, UpdateWReleased;
		public int FixedWDownSteps, UpdateWDownFrames, FixedStepsWhileWHeld;
		public float FixedWheelTotal, UpdateWheelTotal;
		public Vector2 FixedMouseTotal, UpdateMouseTotal;
		public List<float> FixedWheelDeltas { get; } = [];
		public List<float> UpdateWheelDeltas { get; } = [];
		public List<int> FixedStepsPerFrame { get; } = [];

		private bool _wHeld;

		[First]
		public void First(GameTime dt, GameLoopDelegate next)
		{
			_stepsThisFrame = 0;
			next(dt);
		}

		[FixedUpdate]
		public void FixedUpdate(GameTime dt, GameLoopDelegate next)
		{
			_stepsThisFrame++;

			if (input.Pressed(MouseButton.Left)) FixedPressed++;
			if (input.Released(MouseButton.Left)) FixedReleased++;
			if (input.Pressed(Key.Space)) FixedSpacePressed++;
			if (input.Pressed(Key.Space, ModifierKeys.Shift)) FixedShiftSpacePressed++;
			if (input.Pressed(Key.W)) { FixedWPressed++; _wHeld = true; }
			if (input.Released(Key.W)) { FixedWReleased++; _wHeld = false; }
			if (input.Down(Key.W)) FixedWDownSteps++;
			if (_wHeld) FixedStepsWhileWHeld++;

			FixedWheelTotal += input.WheelDelta;
			if (input.WheelDelta != 0) FixedWheelDeltas.Add(input.WheelDelta);
			FixedMouseTotal += input.MouseDelta;

			next(dt);
		}

		[Update]
		public void Update(GameTime dt, GameLoopDelegate next)
		{
			if (input.Pressed(MouseButton.Left)) UpdatePressed++;
			if (input.Released(MouseButton.Left)) UpdateReleased++;
			if (input.Pressed(Key.Space)) UpdateSpacePressed++;
			if (input.Pressed(Key.W)) UpdateWPressed++;
			if (input.Released(Key.W)) UpdateWReleased++;
			if (input.Down(Key.W)) UpdateWDownFrames++;

			UpdateWheelTotal += input.WheelDelta;
			UpdateWheelDeltas.Add(input.WheelDelta);
			UpdateMouseTotal += input.MouseDelta;

			next(dt);
		}

		[Last]
		public void Last(GameTime dt, GameLoopDelegate next)
		{
			FixedStepsPerFrame.Add(_stepsThisFrame);
			next(dt);
		}
	}
}
