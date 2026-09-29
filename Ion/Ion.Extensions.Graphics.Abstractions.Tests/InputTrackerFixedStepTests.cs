using System.Numerics;

namespace Ion.Extensions.Graphics.Abstractions.Tests;

public class InputTrackerFixedStepTests
{
	private sealed class Loop : ILoopContext
	{
		public GameLoopStage Stage { get; set; }
		public uint Frame { get; set; }
		public long FixedStepCount { get; set; }
	}

	private readonly Loop _loop = new();
	private readonly InputTracker _input;

	public InputTrackerFixedStepTests()
	{
		_input = new InputTracker(_loop);
	}

	private void BeginFrame(Action? events = null)
	{
		_loop.Stage = GameLoopStage.First;
		_input.BeginFrame();
		events?.Invoke();
		_loop.Stage = GameLoopStage.Update;
	}

	private void InFixedStep(Action check)
	{
		_loop.FixedStepCount++;
		_loop.Stage = GameLoopStage.FixedUpdate;
		check();
		_loop.Stage = GameLoopStage.Update;
	}

	[Fact]
	public void EdgeOnAFrameWithoutAFixedStepIsCarriedToTheNextFixedStep()
	{
		BeginFrame(() => _input.OnMouseButton(MouseButton.Left, down: true));
		Assert.True(_input.Pressed(MouseButton.Left)); // frame view

		BeginFrame(() => _input.OnMouseButton(MouseButton.Left, down: false));
		Assert.False(_input.Pressed(MouseButton.Left));
		Assert.True(_input.Released(MouseButton.Left));

		InFixedStep(() =>
		{
			Assert.True(_input.Pressed(MouseButton.Left));
			Assert.True(_input.Released(MouseButton.Left));
			Assert.False(_input.Down(MouseButton.Left));
		});

		InFixedStep(() =>
		{
			Assert.False(_input.Pressed(MouseButton.Left));
			Assert.False(_input.Released(MouseButton.Left));
		});
	}

	[Fact]
	public void SeveralFixedStepsInOneFrameShowTheEdgeOnlyToTheFirst()
	{
		BeginFrame(() => _input.OnKey(Key.A, down: true, repeat: false, ModifierKeys.Control));

		InFixedStep(() => Assert.True(_input.Pressed(Key.A, ModifierKeys.Control)));
		InFixedStep(() =>
		{
			Assert.False(_input.Pressed(Key.A));
			Assert.True(_input.Down(Key.A));
		});

		// The frame view still reports the edge for the whole frame.
		Assert.True(_input.Pressed(Key.A));
	}

	[Fact]
	public void ModifiersAccumulateUntilTheFixedStepThenReset()
	{
		BeginFrame(() => _input.OnKey(Key.S, down: true, repeat: false, ModifierKeys.Shift));
		BeginFrame(() =>
		{
			_input.OnKey(Key.S, down: false, repeat: false, ModifierKeys.None);
			_input.OnKey(Key.S, down: true, repeat: false, ModifierKeys.Alt);
		});

		InFixedStep(() =>
		{
			Assert.True(_input.Pressed(Key.S, ModifierKeys.Shift));
			Assert.True(_input.Pressed(Key.S, ModifierKeys.Alt));
		});

		BeginFrame(() => _input.OnKey(Key.S, down: true, repeat: false, ModifierKeys.Control));
		InFixedStep(() =>
		{
			Assert.True(_input.Pressed(Key.S, ModifierKeys.Control));
			Assert.False(_input.Pressed(Key.S, ModifierKeys.Shift));
		});
	}

	[Fact]
	public void DeltasAccumulateBetweenFixedSteps()
	{
		BeginFrame(() => { _input.OnWheel(1); _input.OnMouseMove(new Vector2(4, 0)); });
		BeginFrame(() => { _input.OnWheel(2); _input.OnMouseMove(new Vector2(4, 6)); });

		Assert.Equal(2f, _input.WheelDelta);
		Assert.Equal(new Vector2(0, 6), _input.MouseDelta);

		InFixedStep(() =>
		{
			Assert.Equal(3f, _input.WheelDelta);
			Assert.Equal(new Vector2(4, 6), _input.MouseDelta);
		});

		InFixedStep(() =>
		{
			Assert.Equal(0f, _input.WheelDelta);
			Assert.Equal(Vector2.Zero, _input.MouseDelta);
		});

		BeginFrame();
		Assert.Equal(0f, _input.WheelDelta);
		Assert.Equal(new Vector2(4, 6), _input.MousePosition);
	}

	[Fact]
	public void RepeatsAreNotPressesInEitherView()
	{
		BeginFrame(() => _input.OnKey(Key.D, down: true, repeat: true, ModifierKeys.None));

		Assert.False(_input.Pressed(Key.D));
		InFixedStep(() =>
		{
			Assert.False(_input.Pressed(Key.D));
			Assert.True(_input.Down(Key.D));
		});
	}
}
