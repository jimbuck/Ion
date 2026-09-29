using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.UI.Tests;

public class LayoutTests
{
	private static UiRect Rect(UiHarness h, string path) => h.Node(path).Rect;

	[Fact, Trait(CATEGORY, UNIT)]
	public void GrowSharesTheFreeSpaceByFactor()
	{
		var h = new UiHarness();
		h.Frame(ui =>
		{
			using (ui.Row("row", new UiStyle { Width = 400, Height = 50, Gap = 0 }))
			{
				ui.Column("a", new UiStyle { Grow = 1 }).Dispose();
				ui.Column("b", new UiStyle { Grow = 3 }).Dispose();
			}
		});

		Assert.Equal(new UiRect(0, 0, 100, 50), Rect(h, "row/a"));
		Assert.Equal(new UiRect(100, 0, 300, 50), Rect(h, "row/b"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void GrowStopsAtTheMaximumAndSharesTheRestAgain()
	{
		var h = new UiHarness();
		h.Frame(ui =>
		{
			using (ui.Row("row", new UiStyle { Width = 400, Height = 50, Gap = 10 }))
			{
				ui.Column("a", new UiStyle { Grow = 1, MaxWidth = 50 }).Dispose();
				ui.Column("b", new UiStyle { Grow = 1 }).Dispose();
				ui.Column("c", new UiStyle { Width = 40 }).Dispose();
			}
		});

		Assert.Equal(50, Rect(h, "row/a").Width);
		// 400 - 50 - 40 - 2 gaps of 10.
		Assert.Equal(new UiRect(60, 0, 290, 50), Rect(h, "row/b"));
		Assert.Equal(new UiRect(360, 0, 40, 50), Rect(h, "row/c"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MinimumWinsOverFixedAndMaximumSizes()
	{
		var h = new UiHarness();
		h.Frame(ui =>
		{
			using (ui.Row("row", new UiStyle { Width = 100, MaxWidth = 60, MinWidth = 80, Height = 20 }))
			{
				ui.Column("a", new UiStyle { Width = 30, MinWidth = 45 }).Dispose();
				ui.Column("b", new UiStyle { Width = 300, MaxWidth = 20 }).Dispose();
			}
		});

		Assert.Equal(80, Rect(h, "row").Width);
		Assert.Equal(45, Rect(h, "row/a").Width);
		Assert.Equal(20, Rect(h, "row/b").Width);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(UiJustify.Start, 0, 50, 100)]
	[InlineData(UiJustify.Center, 75, 125, 175)]
	[InlineData(UiJustify.End, 150, 200, 250)]
	[InlineData(UiJustify.SpaceBetween, 0, 125, 250)]
	[InlineData(UiJustify.SpaceAround, 25, 125, 225)]
	public void JustifyPlacesTheFreeSpace(UiJustify justify, float a, float b, float c)
	{
		var h = new UiHarness();
		h.Frame(ui =>
		{
			using (ui.Row("row", new UiStyle { Width = 300, Height = 20, Gap = 0, Justify = justify }))
			{
				ui.Column("a", new UiStyle { Width = 50 }).Dispose();
				ui.Column("b", new UiStyle { Width = 50 }).Dispose();
				ui.Column("c", new UiStyle { Width = 50 }).Dispose();
			}
		});

		Assert.Equal(a, Rect(h, "row/a").X);
		Assert.Equal(b, Rect(h, "row/b").X);
		Assert.Equal(c, Rect(h, "row/c").X);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CrossAxisStretchesByDefaultAndAlignsOnRequest()
	{
		var h = new UiHarness();
		h.Frame(ui =>
		{
			using (ui.Row("row", new UiStyle { Width = 300, Height = 100, Gap = 0 }))
			{
				ui.Column("stretched", new UiStyle { Width = 50 }).Dispose();
				ui.Column("capped", new UiStyle { Width = 50, MaxHeight = 30 }).Dispose();
				ui.Column("centered", new UiStyle { Width = 50, Height = 20, AlignSelf = UiAlignSelf.Center }).Dispose();
				ui.Column("end", new UiStyle { Width = 50, Height = 20, AlignSelf = UiAlignSelf.End }).Dispose();
			}
		});

		Assert.Equal(new UiRect(0, 0, 50, 100), Rect(h, "row/stretched"));
		Assert.Equal(30, Rect(h, "row/capped").Height);
		Assert.Equal(new UiRect(100, 40, 50, 20), Rect(h, "row/centered"));
		Assert.Equal(new UiRect(150, 80, 50, 20), Rect(h, "row/end"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void PaddingAndGapSeparateChildrenAndSizeTheContainer()
	{
		var h = new UiHarness();
		h.Frame(ui =>
		{
			using (ui.Panel("panel", new UiStyle { Padding = new UiThickness(10, 12, 10, 12), Gap = 5, AlignSelf = UiAlignSelf.Start }))
			{
				ui.Label("ab", "one");
				ui.Label("abcd", "two");
			}
		});

		// Labels: 8 px per character, 16 px tall (the null font at size 16).
		Assert.Equal(new UiRect(10, 12, 32, 16), Rect(h, "panel/one"));
		Assert.Equal(new UiRect(10, 33, 32, 16), Rect(h, "panel/two"));
		Assert.Equal(new UiRect(0, 0, 52, 12 + 16 + 5 + 16 + 12), Rect(h, "panel"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WrappingMovesChildrenThatDoNotFitToTheNextLine()
	{
		var h = new UiHarness();
		h.Frame(ui =>
		{
			using (ui.Row("row", new UiStyle { Width = 200, Gap = 10, Wrap = true, AlignItems = UiAlign.Start, AlignSelf = UiAlignSelf.Start }))
			{
				for (var i = 0; i < 5; i++) ui.Column(null, new UiStyle { Width = 80, Height = 20 }).Dispose();
			}
		});

		Assert.Equal(new UiRect(0, 0, 80, 20), Rect(h, "row/column"));
		Assert.Equal(new UiRect(90, 0, 80, 20), Rect(h, "row/column#2"));
		Assert.Equal(new UiRect(0, 30, 80, 20), Rect(h, "row/column#3"));
		Assert.Equal(new UiRect(90, 30, 80, 20), Rect(h, "row/column#4"));
		Assert.Equal(new UiRect(0, 60, 80, 20), Rect(h, "row/column#5"));
		Assert.Equal(80, Rect(h, "row").Height);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RootStyleCentersTheMenu()
	{
		var h = new UiHarness(800, 600);
		h.Ui.RootStyle = new UiStyle { Justify = UiJustify.Center, AlignItems = UiAlign.Center };
		h.Frame(ui =>
		{
			using (ui.Panel("menu", new UiStyle { Width = 200, Height = 100 })) { }
		});

		Assert.Equal(new UiRect(300, 250, 200, 100), Rect(h, "menu"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WidgetsSizeFromTheThemeAndTheirText()
	{
		var h = new UiHarness();
		h.Frame(ui =>
		{
			using (ui.Column("c", new UiStyle { AlignItems = UiAlign.Start }))
			{
				ui.Button("Play");
				var on = true;
				ui.Toggle("Sound", ref on);
				var volume = 0.5f;
				ui.Slider("Vol", ref volume);
			}
		});

		var theme = UiTheme.Default;
		Assert.Equal(new UiRect(0, 0, 32 + 2 * theme.ButtonPadding, theme.ControlHeight), Rect(h, "c/Play"));
		Assert.Equal(theme.ToggleSize + theme.Gap + 40, Rect(h, "c/Sound").Width);
		Assert.Equal(24 + theme.Gap + theme.SliderWidth + theme.Gap + theme.ValueWidth, Rect(h, "c/Vol").Width);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScrollViewClipsAndScrollsItsContent()
	{
		var h = new UiHarness();
		void Build(Ui ui)
		{
			using (ui.ScrollView("scroll", new UiStyle { Height = 100, Width = 200, Gap = 0 }))
			{
				for (var i = 0; i < 10; i++) ui.Column(null, new UiStyle { Height = 40 }).Dispose();
			}
		}

		h.Frame(Build);
		Assert.Equal("0", h.Node("scroll").Value);
		Assert.True(h.Node("scroll/column#3").Visible);
		Assert.False(h.Node("scroll/column#4").Visible);

		// The scissor segment of the scroll view.
		Assert.Contains(h.Batch.Segments, s => s.Scissor == new Rectangle(0, 0, 200, 100));

		// Wheel down over it: 40 px per notch.
		h.Input.SetMousePosition(new Vector2(50, 50));
		h.Input.Scroll(-2);
		h.Frame(Build);
		Assert.Equal("80", h.Node("scroll").Value);
		Assert.Equal(-80, h.Node("scroll/column").Rect.Y);
		Assert.True(h.Node("scroll/column#5").Visible);

		// Clamped at the end: 10 * 40 - 100.
		h.Input.Scroll(-100);
		h.Frame(Build);
		Assert.Equal("300", h.Node("scroll").Value);
	}
}
