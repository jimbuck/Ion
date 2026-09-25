using Microsoft.Extensions.Configuration;

using Ion.Extensions.Windowing;

using SilkButtonName = Silk.NET.Input.ButtonName;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Ion.Extensions.Graphics.Vulkan.Tests;

/// <summary>
/// The Silk.NET input module's mapping and queueing, without a window.
/// </summary>
public class SilkInputTests
{
	private static (SilkInputState Input, InputTracker Tracker) CreateInput()
	{
		var config = new ConfigurationBuilder().Build();
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IEvents>(new EventBus());
		services.AddSilkWindowing(config);
		var provider = services.BuildServiceProvider();
		return (provider.GetRequiredService<SilkInputState>(), provider.GetRequiredService<InputTracker>());
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(SilkKey.A, Key.A)]
	[InlineData(SilkKey.Z, Key.Z)]
	[InlineData(SilkKey.Number0, Key.Number0)]
	[InlineData(SilkKey.Number9, Key.Number9)]
	[InlineData(SilkKey.F1, Key.F1)]
	[InlineData(SilkKey.F25, Key.F25)]
	[InlineData(SilkKey.Keypad7, Key.Keypad7)]
	[InlineData(SilkKey.Space, Key.Space)]
	[InlineData(SilkKey.Escape, Key.Escape)]
	[InlineData(SilkKey.Backspace, Key.BackSpace)]
	[InlineData(SilkKey.Left, Key.Left)]
	[InlineData(SilkKey.SuperLeft, Key.WinLeft)]
	[InlineData(SilkKey.ControlRight, Key.ControlRight)]
	[InlineData(SilkKey.GraveAccent, Key.Grave)]
	[InlineData(SilkKey.Equal, Key.Plus)]
	[InlineData(SilkKey.Unknown, Key.Unknown)]
	public void MapsKeys(SilkKey silk, Key expected)
	{
		Assert.Equal(expected, SilkInputState.MapKey(silk));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EveryKnownSilkKeyMapsToADistinctIonKey()
	{
		var unmapped = new[] { SilkKey.Unknown, SilkKey.World2, SilkKey.KeypadEqual };
		var seen = new Dictionary<Key, SilkKey>();
		foreach (var silk in Enum.GetValues<SilkKey>().Distinct())
		{
			if (unmapped.Contains(silk)) continue;
			var key = SilkInputState.MapKey(silk);
			Assert.True(key != Key.Unknown, $"{silk} is not mapped.");
			Assert.True(seen.TryAdd(key, silk), $"{silk} and {(seen.TryGetValue(key, out var other) ? other : default)} both map to {key}.");
		}
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(SilkMouseButton.Left, MouseButton.Left)]
	[InlineData(SilkMouseButton.Middle, MouseButton.Middle)]
	[InlineData(SilkMouseButton.Right, MouseButton.Right)]
	[InlineData(SilkMouseButton.Button4, MouseButton.Button1)]
	[InlineData(SilkMouseButton.Button12, MouseButton.Button9)]
	public void MapsMouseButtons(SilkMouseButton silk, MouseButton expected)
	{
		Assert.Equal(expected, SilkInputState.MapMouseButton(silk));
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(SilkButtonName.A, GamepadButton.A)]
	[InlineData(SilkButtonName.Y, GamepadButton.Y)]
	[InlineData(SilkButtonName.LeftBumper, GamepadButton.LeftShoulder)]
	[InlineData(SilkButtonName.Home, GamepadButton.Guide)]
	[InlineData(SilkButtonName.DPadLeft, GamepadButton.DPadLeft)]
	public void MapsGamepadButtons(SilkButtonName silk, GamepadButton expected)
	{
		Assert.Equal(expected, SilkInputState.MapGamepadButton(silk));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void QueuedEventsApplyAfterTheTrackerFrameBegins()
	{
		var (input, tracker) = CreateInput();

		// Callbacks arrive during DoEvents, before the input step: they must survive BeginFrame.
		input.KeyDown(SilkKey.ShiftLeft);
		input.KeyDown(SilkKey.S);
		input.Enqueue(InputEvent.ForText('S'));
		input.Enqueue(InputEvent.ForMouseMove(new Vector2(10, 20)));
		Assert.Equal(4, input.PendingEvents);

		input.Step();

		Assert.Equal(0, input.PendingEvents);
		Assert.True(tracker.Pressed(Key.S));
		Assert.True(tracker.Pressed(Key.S, ModifierKeys.Shift));
		Assert.Equal("S", tracker.Text.ToString());
		Assert.Equal(new Vector2(10, 20), input.MousePosition);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ASecondKeyDownIsARepeat()
	{
		var (input, tracker) = CreateInput();
		input.KeyDown(SilkKey.A);
		input.Step();
		Assert.True(tracker.Pressed(Key.A));

		input.KeyDown(SilkKey.A);
		input.Step();
		Assert.False(tracker.Pressed(Key.A));
		Assert.True(tracker.Down(Key.A));

		input.KeyUp(SilkKey.A);
		input.Step();
		Assert.True(tracker.Released(Key.A));
		Assert.False(tracker.Down(Key.A));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void GamepadEventsReachTheTracker()
	{
		var (input, tracker) = CreateInput();
		input.Enqueue(InputEvent.ForGamepadConnection(1, true));
		input.Enqueue(InputEvent.ForGamepadButton(1, GamepadButton.A, true));
		input.Enqueue(InputEvent.ForGamepadAxis(1, GamepadAxis.LeftX, 1f));
		input.Step();

		var pad = tracker.Gamepad(1);
		Assert.True(pad.IsConnected);
		Assert.True(pad.Pressed(GamepadButton.A));
		Assert.Single(tracker.Gamepads);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(WindowPlatform.Glfw, WindowPlatform.Glfw)]
	[InlineData(WindowPlatform.Sdl, WindowPlatform.Sdl)]
	public void ExplicitPlatformsResolveToThemselves(WindowPlatform requested, WindowPlatform expected)
	{
		Assert.Equal(expected, SilkWindow.ResolvePlatform(requested));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AutoIsGlfwOnDesktop()
	{
		Assert.Equal(WindowPlatform.Glfw, SilkWindow.ResolvePlatform(WindowPlatform.Auto));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheWindowBindsItsConfiguration()
	{
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Ion:Window:Platform"] = "Sdl",
			["Ion:Window:Resizable"] = "false",
			["Ion:Window:WindowState"] = "BorderlessFullScreen",
		}).Build();
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IEvents>(new EventBus());
		services.AddSilkWindowing(config);
		var window = services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Options.IOptions<WindowConfig>>().Value;

		Assert.Equal(WindowPlatform.Sdl, window.Platform);
		Assert.False(window.Resizable);
		Assert.Equal(WindowState.BorderlessFullScreen, window.WindowState);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void BeforeCreationTheWindowIsEmpty()
	{
		var config = new ConfigurationBuilder().Build();
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IEvents>(new EventBus());
		services.AddSilkWindowing(config);
		var provider = services.BuildServiceProvider();
		var window = provider.GetRequiredService<SilkWindow>();

		Assert.False(window.IsCreated);
		Assert.Null(window.PlatformWindow);
		Assert.Equal(Vector2.Zero, window.FramebufferSize);
		Assert.Equal(NativeWindowKind.None, window.NativeHandles.Kind);
		Assert.Same(window, provider.GetRequiredService<IWindowSurface>());
		Assert.Same(window, provider.GetRequiredService<IWindow>());
	}
}
