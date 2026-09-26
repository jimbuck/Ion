using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.UI.Tests;

public class HitTestTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void ClickingAButtonReportsItOnceInTheFrameOfTheRelease()
	{
		var h = new UiHarness();
		var clicks = 0;
		void Build(Ui ui)
		{
			using (ui.Panel("menu"))
			{
				if (ui.Button("Play")) clicks++;
				ui.Button("Quit");
			}
		}

		h.Frame(Build);
		h.ClickAt("menu/Play");
		h.Frame(Build);
		Assert.Equal(1, clicks);
		h.Frames(3, Build);
		Assert.Equal(1, clicks);
		Assert.Equal("menu/Play", h.Tree.FocusedPath);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void APressThatStartedElsewhereIsNotAClick()
	{
		var h = new UiHarness();
		var clicks = 0;
		void Build(Ui ui)
		{
			using (ui.Panel("menu"))
			{
				if (ui.Button("Play")) clicks++;
			}
		}

		h.Frame(Build);
		var play = h.Node("menu/Play").Rect;
		h.Input.SetMousePosition(new Vector2(700, 500));
		h.Input.Press(MouseButton.Left);
		h.Frame(Build);
		h.Input.SetMousePosition(new Vector2(play.CenterX, play.CenterY));
		h.Input.Release(MouseButton.Left);
		h.Frame(Build);
		Assert.Equal(0, clicks);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DisabledWidgetsIgnoreThePointer()
	{
		var h = new UiHarness();
		var clicks = 0;
		void Build(Ui ui)
		{
			using (ui.Disabled())
			{
				if (ui.Button("Play")) clicks++;
			}
		}

		h.Frame(Build);
		Assert.False(h.Node("Play").Enabled);
		h.ClickAt("Play");
		h.Frames(2, Build);
		Assert.Equal(0, clicks);
		Assert.False(h.Tree.Click("Play"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void PanelsBlockThePointerButTheirWidgetsDoNot()
	{
		var h = new UiHarness();
		void Build(Ui ui)
		{
			using (ui.Panel("panel", new UiStyle { Width = 300, Height = 200 }))
			{
				ui.Button("OK");
			}
		}

		h.Frame(Build);
		h.Input.SetMousePosition(new Vector2(150, 150));
		h.Frame(Build);
		Assert.True(h.Ui.IsPointerOverUi);
		Assert.Equal(0u, h.Ui.HotId);

		var ok = h.Node("panel/OK").Rect;
		h.Input.SetMousePosition(new Vector2(ok.CenterX, ok.CenterY));
		h.Frame(Build);
		Assert.NotEqual(0u, h.Ui.HotId);

		h.Input.SetMousePosition(new Vector2(700, 500));
		h.Frame(Build);
		Assert.False(h.Ui.IsPointerOverUi);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WidgetsScrolledOutOfAScrollViewCannotBeClicked()
	{
		var h = new UiHarness();
		var clicked = new bool[6];
		void Build(Ui ui)
		{
			using (ui.ScrollView("scroll", new UiStyle { Height = 80, Width = 200, Gap = 0 }))
			{
				for (var i = 0; i < clicked.Length; i++)
				{
					if (ui.Button("Item", key: "item" + i)) clicked[i] = true;
				}
			}

			ui.Column("below", new UiStyle { Height = 300 }).Dispose();
		}

		h.Frame(Build);
		// item2 lies at y 72..108 under the scroll view's bottom edge (80): only its visible part is clickable.
		var item3 = h.Node("scroll/item3").Rect;
		Assert.False(h.Node("scroll/item3").Visible);
		h.Input.SetMousePosition(new Vector2(item3.CenterX, item3.CenterY));
		h.Input.Click();
		h.Frame(Build);
		Assert.DoesNotContain(true, clicked);

		h.ClickAt("scroll/item1");
		h.Frame(Build);
		Assert.True(clicked[1]);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DraggingASliderFollowsThePointerAcrossItsTrack()
	{
		var h = new UiHarness();
		var value = 0f;
		void Build(Ui ui) => ui.Slider("Volume", ref value, 0, 100, 1, style: new UiStyle { AlignSelf = UiAlignSelf.Start });

		h.Frame(Build);
		var slider = h.Node("Volume").Rect;
		var theme = UiTheme.Default;
		var trackX = slider.X + 48 + theme.Gap;
		var trackWidth = theme.SliderWidth;

		h.Input.SetMousePosition(new Vector2(trackX + trackWidth * 0.25f, slider.CenterY));
		h.Input.Press(MouseButton.Left);
		h.Frame(Build);
		Assert.Equal(25, value);

		h.Input.SetMousePosition(new Vector2(trackX + trackWidth * 2, slider.CenterY));
		h.Frame(Build);
		Assert.Equal(100, value);

		h.Input.Release(MouseButton.Left);
		h.Frame(Build);
		h.Input.SetMousePosition(new Vector2(trackX, slider.CenterY));
		h.Frame(Build);
		Assert.Equal(100, value);
		Assert.Equal("100", h.Node("Volume").Value);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ClickingAToggleAndAListItemChangesTheirValues()
	{
		var h = new UiHarness();
		var on = false;
		var selected = 0;
		string[] items = ["Easy", "Normal", "Hard"];
		void Build(Ui ui)
		{
			ui.Toggle("Music", ref on);
			ui.List("Difficulty", items, ref selected);
		}

		h.Frame(Build);
		h.ClickAt("Music");
		h.Frame(Build);
		Assert.True(on);
		Assert.Equal("true", h.Node("Music").Value);

		h.ClickAt("Difficulty/Hard");
		h.Frame(Build);
		Assert.Equal(2, selected);
		Assert.Equal("Hard", h.Node("Difficulty").Value);
		Assert.Equal("true", h.Node("Difficulty/Hard").Value);
		Assert.Equal("false", h.Node("Difficulty/Easy").Value);
	}
}

public class FocusNavigationTests
{
	private static void Menu(Ui ui)
	{
		using (ui.Panel("menu"))
		{
			ui.Button("Play");
			ui.Button("Options");
			using (ui.Row("row"))
			{
				ui.Button("Left");
				ui.Button("Right");
			}

			ui.Button("Quit");
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheFirstFocusableWidgetIsFocusedAutomatically()
	{
		var h = new UiHarness();
		h.Frame(Menu);
		Assert.Equal("menu/Play", h.Tree.FocusedPath);
		Assert.True(h.Node("menu/Play").Focused);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AutoFocusCanBeTurnedOff()
	{
		var h = new UiHarness(options: new UiOptions { AutoFocus = false });
		h.Frame(Menu);
		Assert.Null(h.Tree.FocusedPath);
		h.Input.Tap(Key.Down);
		h.Frame(Menu);
		Assert.Equal("menu/Play", h.Tree.FocusedPath);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ArrowKeysMoveSpatiallyInLayoutOrder()
	{
		var h = new UiHarness();
		h.Frame(Menu);
		var order = new List<string?>();
		Key[] keys = [Key.Down, Key.Down, Key.Right, Key.Down, Key.Down, Key.Up, Key.Up, Key.Left, Key.Up];
		foreach (var key in keys)
		{
			h.Input.Tap(key);
			h.Frame(Menu);
			order.Add(h.Tree.FocusedPath);
		}

		Assert.Equal(
		[
			"menu/Options",
			"menu/row/Left",
			"menu/row/Right",
			"menu/Quit",
			"menu/Quit",
			"menu/row/Left",
			"menu/Options",
			"menu/Options",
			"menu/Play",
		], order);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TabFollowsTreeOrderAndWraps()
	{
		var h = new UiHarness();
		h.Frame(Menu);
		var order = new List<string?>();
		for (var i = 0; i < 5; i++)
		{
			h.Input.Tap(Key.Tab);
			h.Frame(Menu);
			order.Add(h.Tree.FocusedPath);
		}

		h.Input.Tap(Key.Tab, ModifierKeys.Shift);
		h.Input.Press(Key.ShiftLeft);
		h.Frame(Menu);
		order.Add(h.Tree.FocusedPath);

		Assert.Equal(["menu/Options", "menu/row/Left", "menu/row/Right", "menu/Quit", "menu/Play", "menu/Quit"], order);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void GamepadDPadNavigatesAndAActivates()
	{
		var h = new UiHarness();
		var quit = 0;
		void Build(Ui ui)
		{
			using (ui.Panel("menu"))
			{
				ui.Button("Play");
				if (ui.Button("Quit")) quit++;
			}
		}

		h.Input.ConnectGamepad(0);
		h.Frame(Build);
		h.Input.Tap(0, GamepadButton.DPadDown);
		h.Frame(Build);
		Assert.Equal("menu/Quit", h.Tree.FocusedPath);
		h.Input.Tap(0, GamepadButton.A);
		h.Frame(Build);
		Assert.Equal(1, quit);

		// The left stick navigates on crossing its threshold.
		h.Input.SetLeftStick(0, new Vector2(0, -1));
		h.Frame(Build);
		Assert.Equal("menu/Play", h.Tree.FocusedPath);
		h.Frame(Build);
		Assert.Equal("menu/Play", h.Tree.FocusedPath);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void BackIsReportedByEscapeAndGamepadB()
	{
		var h = new UiHarness();
		h.Input.ConnectGamepad(0);
		h.Frame(Menu);
		Assert.False(h.Ui.BackPressed);
		h.Input.Tap(Key.Escape);
		h.Frame(Menu);
		Assert.True(h.Ui.BackPressed);
		h.Frame(Menu);
		Assert.False(h.Ui.BackPressed);
		h.Input.Tap(0, GamepadButton.B);
		h.Frame(Menu);
		Assert.True(h.Ui.BackPressed);
		h.Tree.Back();
		h.Frame(Menu);
		Assert.True(h.Ui.BackPressed);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AHeldDirectionRepeatsAfterTheDelay()
	{
		var h = new UiHarness();
		void Build(Ui ui)
		{
			for (var i = 0; i < 10; i++) ui.Button("B", key: "b" + i);
		}

		h.Frame(Build);
		h.Input.Press(Key.Down);
		h.Frame(Build);
		Assert.Equal("b1", h.Tree.FocusedPath);

		// Held: nothing until the repeat delay (0.4 s = 24 frames at 60 Hz), then every 0.08 s (about 5 frames).
		h.Frames(22, Build);
		Assert.Equal("b1", h.Tree.FocusedPath);
		h.Frames(4, Build);
		Assert.Equal("b2", h.Tree.FocusedPath);
		h.Frames(6, Build);
		Assert.Equal("b3", h.Tree.FocusedPath);
		h.Input.Release(Key.Down);
		h.Frames(30, Build);
		Assert.Equal("b3", h.Tree.FocusedPath);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LeftAndRightAdjustAFocusedSlider()
	{
		var h = new UiHarness();
		var volume = 0.5f;
		void Build(Ui ui)
		{
			ui.Slider("Volume", ref volume, 0, 1, 0.1f);
			ui.Button("Back");
		}

		h.Frame(Build);
		Assert.Equal("Volume", h.Tree.FocusedPath);
		h.Input.Tap(Key.Right);
		h.Frame(Build);
		Assert.Equal(0.6f, volume, 5);
		h.Input.Tap(Key.Left);
		h.Frame(Build);
		h.Input.Tap(Key.Left);
		h.Frame(Build);
		Assert.Equal(0.4f, volume, 5);
		Assert.Equal("0.4", h.Node("Volume").Value);
		h.Input.Tap(Key.Down);
		h.Frame(Build);
		Assert.Equal("Back", h.Tree.FocusedPath);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FocusMovingIntoAScrollViewScrollsTheWidgetIntoView()
	{
		var h = new UiHarness();
		void Build(Ui ui)
		{
			using (ui.ScrollView("scroll", new UiStyle { Height = 100, Gap = 0 }))
			{
				for (var i = 0; i < 8; i++) ui.Button("B", key: "b" + i);
			}
		}

		h.Frame(Build);
		for (var i = 0; i < 4; i++)
		{
			h.Input.Tap(Key.Down);
			h.Frame(Build);
		}

		var focused = h.Node("scroll/b4");
		Assert.True(focused.Focused);
		var view = h.Node("scroll").Rect;
		Assert.True(focused.Rect.Bottom <= view.Bottom + 0.01f, $"{focused.Rect} in {view}");
		Assert.Equal("80", h.Node("scroll").Value);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FocusMovesOnWhenTheFocusedWidgetDisappears()
	{
		var h = new UiHarness();
		var options = false;
		void Build(Ui ui)
		{
			if (options) ui.Button("Back");
			else Menu(ui);
		}

		h.Frame(Build);
		h.Input.Tap(Key.Down);
		h.Frame(Build);
		Assert.Equal("menu/Options", h.Tree.FocusedPath);
		options = true;
		h.Frame(Build);
		Assert.Equal("Back", h.Tree.FocusedPath);
	}
}
