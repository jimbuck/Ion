using Ion.Extensions.Graphics;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.UI.Tests;

public class DrawTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void WidgetsDrawRectanglesAndCenteredTextInTreeOrder()
	{
		var h = new UiHarness();
		h.Frame(ui =>
		{
			using (ui.Panel("menu", new UiStyle { AlignSelf = UiAlignSelf.Start }))
			{
				ui.Label("Title");
				ui.Button("Play");
			}
		});

		var draws = h.Batch.Draws;
		Assert.Equal(("rect", UiTheme.Default.PanelBackground), (draws[0].Kind, draws[0].Color));
		Assert.Equal(["Title", "Play"], h.Batch.Strings);
		var play = h.Node("menu/Play").Rect;
		var text = draws.Single(d => d.Text == "Play").Rect;
		Assert.Equal(play.CenterX, text.X + text.Width / 2, 1);
		Assert.Equal(play.CenterY, text.Y + text.Height / 2, 1);
		Assert.Equal(0, h.Batch.Depth);
		Assert.Single(h.Batch.Segments);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TextIsSkippedWithoutAFontButLayoutStillWorks()
	{
		var h = new UiHarness();
		h.Ui.Theme = UiTheme.Default;
		h.Frame(ui => ui.Button("Play", style: new UiStyle { AlignSelf = UiAlignSelf.Start }));
		Assert.Empty(h.Batch.Strings);
		Assert.Equal(4 * 8 + 2 * UiTheme.Default.ButtonPadding, h.Node("Play").Rect.Width);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NineSlicesKeepTheirCornersAndStretchTheRest()
	{
		var batch = new RecordingBatch();
		var skin = new UiNineSlice(new FakeTexture(32, 32), new UiThickness(8), 2f);
		Ui.DrawNineSlice(batch, skin, new RectangleF(10, 20, 100, 60), Color.White);

		Assert.Equal(9, batch.Draws.Count);
		var corner = batch.Draws[0];
		Assert.Equal(new RectangleF(10, 20, 16, 16), corner.Rect);
		Assert.Equal(new RectangleF(0, 0, 8, 8), corner.Source);
		var center = batch.Draws[4];
		Assert.Equal(new RectangleF(26, 36, 68, 28), center.Rect);
		Assert.Equal(new RectangleF(8, 8, 16, 16), center.Source);
		var last = batch.Draws[8];
		Assert.Equal(new RectangleF(94, 64, 16, 16), last.Rect);
		Assert.Equal(new RectangleF(24, 24, 8, 8), last.Source);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SkinsReplaceTheSolidBackgrounds()
	{
		var h = new UiHarness();
		var skin = new UiNineSlice(new FakeTexture(24, 24), new UiThickness(4));
		h.Ui.Theme = h.Ui.Theme with { PanelSkin = skin, ButtonSkin = skin };
		h.Frame(ui =>
		{
			using (ui.Panel("p")) ui.Button("B");
		});

		Assert.Equal(18, h.Batch.Draws.Count(d => d.Kind == "sprite"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScrollViewContentIsDrawnInAScissorSegmentAndCulled()
	{
		var h = new UiHarness();
		h.Frame(ui =>
		{
			using (ui.ScrollView("list", new UiStyle { Height = 100, Width = 300, Gap = 0 }))
			{
				for (var i = 0; i < 20; i++) ui.Label("Row", "row" + i, style: new UiStyle { Height = 25 });
			}
		});

		Assert.Equal(2, h.Batch.Segments.Count);
		Assert.Equal(new Rectangle(0, 0, 300, 100), h.Batch.Segments[1].Scissor);
		// Only the rows inside the view are drawn.
		Assert.Equal(4, h.Batch.Strings.Count());
		// The scroll bar: a quarter of the view (100 of 500 px).
		var bar = h.Batch.Draws.Last();
		Assert.Equal(UiTheme.Default.ScrollBar, bar.Color);
		Assert.Equal(20, bar.Rect.Height);
	}
}

public class AllocationTests
{
	private sealed class Screen
	{
		private static readonly string[] Items = ["Easy", "Normal", "Hard", "Nightmare"];
		public bool Music = true;
		public float Volume = 0.5f;
		public int Difficulty = 1;
		public string Name = "Player";
		public int Frame;
		public int Clicks;

		public void Build(Ui ui)
		{
			Frame++;
			using (ui.Panel("options", new UiStyle { Width = 600 }))
			{
				ui.Label("Options", scale: 2);
				Span<char> buffer = stackalloc char[16];
				(Frame % 50).TryFormat(buffer, out var written);
				ui.Label(buffer[..written], "frame");
				ui.Toggle("Music", ref Music);
				ui.Slider("Volume", ref Volume, 0, 1);
				ui.List("Difficulty", Items, ref Difficulty);
				ui.TextInput("Name", ref Name);
				using (ui.ScrollView("scroll", new UiStyle { Height = 120 }))
				{
					for (var i = 0; i < 12; i++)
					{
						if (ui.Button("Item")) Clicks++;
					}
				}

				using (ui.Row("buttons", new UiStyle { Wrap = true, Justify = UiJustify.SpaceBetween }))
				{
					using (ui.Disabled()) ui.Button("Apply");
					ui.Spacer();
					if (ui.Button("Back")) Clicks++;
				}
			}
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AFrameAllocatesNothingInSteadyState()
	{
		var h = new UiHarness();
		var screen = new Screen();
		Action<Ui> build = screen.Build;
		var batch = new CountingBatch();
		var ui = h.Ui;
		var input = h.Input;
		input.ConnectGamepad(0);

		void Frame()
		{
			input.Step();
			ui.BeginFrame(UiHarness.FrameSeconds, h.Viewport);
			build(ui);
			ui.EndFrame();
			ui.Draw(batch);
		}

		// Warm up: every widget, every formatted value, focus moved through the screen, the scroll view scrolled.
		for (var i = 0; i < 60; i++) Frame();
		for (var i = 0; i < 12; i++)
		{
			input.Tap(Key.Down);
			Frame();
		}

		input.SetMousePosition(new Vector2(100, 300));
		for (var i = 0; i < 60; i++) Frame();

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 200; i++) Frame();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.True(batch.Calls > 0);
		Assert.True(ui.NodeCount > 25);
	}
}
