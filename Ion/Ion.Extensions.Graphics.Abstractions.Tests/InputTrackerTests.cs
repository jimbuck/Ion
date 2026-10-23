namespace Ion.Extensions.Graphics.Abstractions.Tests;

public class InputTrackerTests
{
	private readonly InputTracker _input = new();

	private void Frame(Action events)
	{
		_input.BeginFrame();
		events();
	}

	[Fact]
	public void KeyPress_IsPressedAndDownForOneFrame()
	{
		Frame(() => _input.OnKey(Key.A, down: true, repeat: false, ModifierKeys.None));
		Assert.True(_input.Pressed(Key.A));
		Assert.True(_input.Down(Key.A));
		Assert.False(_input.Released(Key.A));

		Frame(() => { });
		Assert.False(_input.Pressed(Key.A));
		Assert.True(_input.Down(Key.A));

		Frame(() => _input.OnKey(Key.A, down: false, repeat: false, ModifierKeys.None));
		Assert.True(_input.Released(Key.A));
		Assert.False(_input.Down(Key.A));
	}

	[Fact]
	public void KeyPressAndReleaseInSameFrame_ReportsBothEdges()
	{
		Frame(() =>
		{
			_input.OnKey(Key.Space, down: true, repeat: false, ModifierKeys.None);
			_input.OnKey(Key.Space, down: false, repeat: false, ModifierKeys.None);
		});

		Assert.True(_input.Pressed(Key.Space));
		Assert.True(_input.Released(Key.Space));
		Assert.False(_input.Down(Key.Space));

		Frame(() => { });
		Assert.False(_input.Pressed(Key.Space));
		Assert.False(_input.Released(Key.Space));
	}

	[Fact]
	public void ReleaseThenPressInSameFrame_EndsDown()
	{
		Frame(() => _input.OnKey(Key.W, down: true, repeat: false, ModifierKeys.None));
		Frame(() =>
		{
			_input.OnKey(Key.W, down: false, repeat: false, ModifierKeys.None);
			_input.OnKey(Key.W, down: true, repeat: false, ModifierKeys.None);
		});

		Assert.True(_input.Pressed(Key.W));
		Assert.True(_input.Released(Key.W));
		Assert.True(_input.Down(Key.W));
	}

	[Fact]
	public void RepeatEvents_AreNotPresses()
	{
		Frame(() => _input.OnKey(Key.Left, down: true, repeat: false, ModifierKeys.None));
		Frame(() =>
		{
			_input.OnKey(Key.Left, down: true, repeat: true, ModifierKeys.None);
			_input.OnKey(Key.Left, down: true, repeat: true, ModifierKeys.None);
		});

		Assert.False(_input.Pressed(Key.Left));
		Assert.True(_input.Down(Key.Left));
	}

	[Fact]
	public void RepeatAfterFocusLoss_MarksKeyDownWithoutPress()
	{
		Frame(() => _input.OnKey(Key.Left, down: true, repeat: false, ModifierKeys.None));
		_input.ReleaseAll();
		Frame(() => _input.OnKey(Key.Left, down: true, repeat: true, ModifierKeys.None));

		Assert.False(_input.Pressed(Key.Left));
		Assert.True(_input.Down(Key.Left));
	}

	[Fact]
	public void FocusLoss_ReleasesHeldKeysAndButtons()
	{
		Frame(() =>
		{
			_input.OnKey(Key.D, down: true, repeat: false, ModifierKeys.None);
			_input.OnMouseButton(MouseButton.Left, down: true);
		});

		_input.ReleaseAll();

		Assert.False(_input.Down(Key.D));
		Assert.False(_input.Down(MouseButton.Left));

		Frame(() => { });
		Assert.False(_input.Down(Key.D));
		Assert.False(_input.Released(Key.D));
	}

	[Fact]
	public void Modifiers_MatchAnyRequestedModifier()
	{
		Frame(() => _input.OnKey(Key.S, down: true, repeat: false, ModifierKeys.Control | ModifierKeys.Shift));

		Assert.True(_input.Pressed(Key.S, ModifierKeys.Control));
		Assert.True(_input.Pressed(Key.S, ModifierKeys.Shift | ModifierKeys.Alt));
		Assert.False(_input.Pressed(Key.S, ModifierKeys.Alt));

		Frame(() => _input.OnKey(Key.S, down: false, repeat: false, ModifierKeys.Alt));
		Assert.True(_input.Released(Key.S, ModifierKeys.Alt));
		Assert.False(_input.Released(Key.S, ModifierKeys.Control));
	}

	[Fact]
	public void MouseButtons_TrackEdgesAndLevelConsistently()
	{
		Frame(() => _input.OnMouseButton(MouseButton.Right, down: true));
		Assert.True(_input.Pressed(MouseButton.Right));
		Assert.True(_input.Down(MouseButton.Right));

		Frame(() => { });
		Assert.False(_input.Pressed(MouseButton.Right));
		Assert.True(_input.Down(MouseButton.Right));

		Frame(() =>
		{
			_input.OnMouseButton(MouseButton.Right, down: false);
			_input.OnMouseButton(MouseButton.Right, down: true);
			_input.OnMouseButton(MouseButton.Right, down: false);
		});
		Assert.True(_input.Pressed(MouseButton.Right));
		Assert.True(_input.Released(MouseButton.Right));
		Assert.False(_input.Down(MouseButton.Right));
	}

	[Fact]
	public void OutOfRangeValues_AreIgnored()
	{
		Frame(() =>
		{
			_input.OnKey((Key)10_000, down: true, repeat: false, ModifierKeys.None);
			_input.OnMouseButton((MouseButton)(-1), down: true);
		});

		Assert.False(_input.Pressed((Key)10_000));
		Assert.False(_input.Pressed((Key)10_000, ModifierKeys.Shift));
		Assert.False(_input.Down((MouseButton)(-1)));
	}
}
