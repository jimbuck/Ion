using System.Numerics;

using Ion.Examples.Tests;
using Ion.Extensions.Graphics;
using Ion.Extensions.UI;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Menu.Tests;

/// <summary>
/// The menu sample driven end to end the way an agent drives it over the remote protocol: only through <see cref="IUiTree"/>
/// (paths, values, clicks), never through pixels or scripted input.
/// </summary>
public class MenuTreeTests
{
	private static IonTestHost Host() => new IonTestHost().UseGame(b => MenuApp.Configure(b), a => MenuApp.Use(a));

	/// <summary>Steps until <paramref name="path"/> is in the tree (a screen change shows from the frame after the click).</summary>
	private static void StepUntil(IonTestHost host, IUiTree tree, string path) =>
		Assert.True(host.RunUntil(() => tree.TryFind(path, out _), maxFrames: 5), $"'{path}' did not appear. Tree: {string.Join(", ", tree.Snapshot().Select(n => n.Path))}");

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AnAgentChangesEveryOptionAndPlaysThroughTheTree()
	{
		using var host = Host();
		host.Step();
		var tree = host.Get<IUiTree>();
		var settings = host.Get<MenuSettings>();

		// The main menu.
		Assert.Equal(["main", "main/title", "main/greeting", "main/Play", "main/Options", "main/Quit"], tree.Snapshot().Select(n => n.Path));
		Assert.Equal("main/Play", tree.FocusedPath);
		Assert.Equal("Welcome, Player", Node(tree, "main/greeting").Text);

		// Options.
		Assert.True(tree.Click("main/Options"));
		StepUntil(host, tree, "options/Back");
		Assert.Equal(MenuScreen.Options, host.Get<MenuSystem>().Screen);
		Assert.Equal("options/Fullscreen", tree.FocusedPath);
		Assert.Equal("false", Node(tree, "options/Fullscreen").Value);
		Assert.Equal("0.8", Node(tree, "options/Volume").Value);
		Assert.Equal("Normal", Node(tree, "options/Difficulty").Value);
		Assert.Equal("Player", Node(tree, "options/Name").Value);

		Assert.True(tree.Click("options/Fullscreen"));
		Assert.True(tree.SetValue("options/Volume", "0.35"));
		Assert.True(tree.Click("options/Difficulty/Hard"));
		Assert.True(tree.SetValue("options/Name", "Ada"));
		host.Step();
		Assert.True(tree.Type("options/Name", " L."));
		host.Step();

		Assert.True(settings.Fullscreen);
		Assert.True(host.Window.IsFullscreen);
		Assert.Equal(0.35f, settings.Volume, 5);
		Assert.Equal(2, settings.Difficulty);
		Assert.Equal("Ada L.", settings.PlayerName);
		Assert.Equal("true", Node(tree, "options/Fullscreen").Value);
		Assert.Equal("0.35", Node(tree, "options/Volume").Value);
		Assert.Equal("Hard", Node(tree, "options/Difficulty").Value);
		Assert.Equal("true", Node(tree, "options/Difficulty/Hard").Value);
		Assert.Equal("Ada L.", Node(tree, "options/Name").Value);

		// Back to the main menu, which greets the new name.
		Assert.True(tree.Click("options/Back"));
		StepUntil(host, tree, "main/Play");
		Assert.Equal("Welcome, Ada L.", Node(tree, "main/greeting").Text);
		Assert.False(tree.TryFind("options/Back", out _));
		Assert.False(tree.Click("options/Back"));

		// Play, then back out with the back action (gamepad B, Escape).
		Assert.True(tree.Click("main/Play"));
		StepUntil(host, tree, "play/Back");
		Assert.Equal("Ada L. on Hard", Node(tree, "play/status").Text);
		tree.Back();
		StepUntil(host, tree, "main/Quit");

		// Quit ends the game.
		Assert.True(tree.Click("main/Quit"));
		host.Step();
		Assert.True(host.IsExitRequested);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheTreeIsStableWhileNothingHappens()
	{
		using var host = Host();
		host.Step();
		var tree = host.Get<IUiTree>();
		var version = tree.Version;
		var before = tree.Snapshot();
		host.Step(30);
		Assert.Equal(version, tree.Version);
		Assert.Equal(before, tree.Snapshot());
	}

	private static UiNodeInfo Node(IUiTree tree, string path)
	{
		Assert.True(tree.TryFind(path, out var node), $"No node '{path}'.");
		return node;
	}
}

/// <summary>The same menu driven by scripted devices: the gamepad (the R36S controls), the keyboard and the mouse.</summary>
public class MenuInputTests
{
	private static IonTestHost Host() => new IonTestHost().UseGame(b => MenuApp.Configure(b), a => MenuApp.Use(a));

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheGamepadAloneReachesAndChangesTheOptions()
	{
		using var host = Host();
		host.Input.ConnectGamepad(0);
		host.Step();
		var tree = host.Get<IUiTree>();
		var settings = host.Get<MenuSettings>();

		host.Input.Tap(0, GamepadButton.DPadDown);
		host.Step();
		Assert.Equal("main/Options", tree.FocusedPath);
		host.Input.Tap(0, GamepadButton.A);
		host.Step(2);
		Assert.Equal("options/Fullscreen", tree.FocusedPath);

		host.Input.Tap(0, GamepadButton.A);
		host.Step();
		Assert.True(settings.Fullscreen);

		host.Input.Tap(0, GamepadButton.DPadDown);
		host.Step();
		Assert.Equal("options/Volume", tree.FocusedPath);
		host.Input.Tap(0, GamepadButton.DPadLeft);
		host.Step();
		Assert.Equal(0.75f, settings.Volume, 5);

		// Down to the list's last item and select it.
		for (var i = 0; i < 3; i++)
		{
			host.Input.Tap(0, GamepadButton.DPadDown);
			host.Step();
		}

		Assert.Equal("options/Difficulty/Hard", tree.FocusedPath);
		host.Input.Tap(0, GamepadButton.A);
		host.Step();
		Assert.Equal(2, settings.Difficulty);

		host.Input.Tap(0, GamepadButton.B);
		host.Step(2);
		Assert.Equal(MenuScreen.Main, host.Get<MenuSystem>().Screen);
		Assert.Equal("main/Play", tree.FocusedPath);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheKeyboardTypesTheNameAndTheMouseClicksBack()
	{
		using var host = Host();
		host.Step();
		var tree = host.Get<IUiTree>();
		var settings = host.Get<MenuSettings>();

		host.Input.Tap(Key.Down);
		host.Step();
		host.Input.Tap(Key.Enter);
		host.Step(2);
		Assert.Equal(MenuScreen.Options, host.Get<MenuSystem>().Screen);

		// Tab to the name field (Fullscreen, Volume, the three list items, Name), edit it, leave it.
		for (var i = 0; i < 5; i++)
		{
			host.Input.Tap(Key.Tab);
			host.Step();
		}

		Assert.Equal("options/Name", tree.FocusedPath);
		host.Input.Tap(Key.Enter);
		host.Step();
		for (var i = 0; i < 6; i++)
		{
			host.Input.Tap(Key.BackSpace);
			host.Step();
		}

		host.Input.Type("Grace");
		host.Step();
		host.Input.Tap(Key.Enter);
		host.Step();
		Assert.Equal("Grace", settings.PlayerName);

		Assert.True(tree.TryFind("options/Back", out var back));
		host.Input.SetMousePosition(new Vector2(back.Rect.CenterX, back.Rect.CenterY));
		host.Step();
		host.Input.Click();
		host.Step(2);
		Assert.Equal(MenuScreen.Main, host.Get<MenuSystem>().Screen);
	}
}

/// <summary>The options screen rendered headless (lavapipe on CI) and compared with a committed golden image.</summary>
public class MenuRenderingTests
{
	/// <summary>The sample's window size.</summary>
	public const uint Width = 800, Height = 600;

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void TheOptionsScreenMatchesTheGoldenImage()
	{
		var log = new ErrorLog();
		Screenshot shot;
		using (var host = new IonTestHost().UseGame(b => MenuApp.Configure(b), a => MenuApp.Use(a)))
		{
			log.Attach(host).WithRendering(Width, Height).WithConfiguration("Ion:Graphics:PreferredBackend", nameof(GraphicsBackend.Vulkan));
			host.Step();
			var tree = host.Get<IUiTree>();
			Assert.True(tree.Click("main/Options"));
			host.Step(3);
			Assert.True(tree.TryFind("options/Back", out _));
			shot = host.Screenshot();
		}

		log.AssertClean();
		// The clear color in a corner, the panel in the middle.
		Assert.True(shot.GetPixel(4, 4).MaxChannelDifference(new Rgba8(0x14, 0x17, 0x20, 255)) <= 1, $"background {shot.GetPixel(4, 4)}");
		GoldenImage.AssertMatches(shot, RenderingEnvironment.GoldenPath("menu_options.png"), tolerance: 8, maxMismatchRatio: 0.002);
	}
}
