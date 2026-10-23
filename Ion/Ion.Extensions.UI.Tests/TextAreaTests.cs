using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.UI.Tests;

/// <summary>The multi-line text area: wrapping, line breaks, vertical caret movement and scrolling.</summary>
public class TextAreaTests
{
	private const ModifierKeys Shift = ModifierKeys.Shift;
	private const ModifierKeys Ctrl = ModifierKeys.Control;

	private sealed class Form(int lines = 4)
	{
		public string Notes = "";
		public int Changes;

		public void Build(Ui ui)
		{
			if (ui.TextArea("Notes", ref Notes, maxLength: 200, lines: lines)) Changes++;
			ui.Button("OK");
		}
	}

	private static (UiHarness H, Form Form) Editing(string text, int lines = 4)
	{
		var h = new UiHarness();
		var form = new Form(lines) { Notes = text };
		h.Frame(form.Build);
		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		Assert.True(h.Ui.IsEditingText);
		return (h, form);
	}

	private static void Tap(UiHarness h, Form form, Key key, ModifierKeys modifiers = ModifierKeys.None)
	{
		h.Input.Tap(key, modifiers);
		h.Frame(form.Build);
	}

	/// <summary>The text left edge: the label "Notes" (40 px), the gap and 6 px of padding.</summary>
	private static float TextX(UiHarness h) => h.Node("Notes").Rect.X + 40 + UiTheme.Default.Gap + 6;

	/// <summary>How many 8 px characters fit a line of the field.</summary>
	private static int PerLine(UiHarness h)
	{
		var rect = h.Node("Notes").Rect;
		return (int)((rect.Width - 40 - UiTheme.Default.Gap - 12) / 8);
	}

	/// <summary>The text lines drawn in the field, top to bottom.</summary>
	private static string[] Lines(UiHarness h) =>
		[.. h.Batch.Draws.Where(d => d.Text is { } t && t != "Notes" && t != "OK" && d.Rect.X >= TextX(h) - 0.5f).OrderBy(d => d.Rect.Y).Select(d => d.Text!)];

	[Fact, Trait(CATEGORY, UNIT)]
	public void TextWrapsAtSpacesAndAtLineBreaks()
	{
		var h = new UiHarness();
		var form = new Form { Notes = "" };
		h.Frame(form.Build);
		var perLine = PerLine(h);

		// Words that do not fit move to the next line whole; a word longer than a line is cut; \n always breaks.
		var word = new string('w', perLine - 4);
		form.Notes = $"{word} abcdef\n{new string('x', perLine + 3)}";
		h.Frame(form.Build);
		Assert.Equal([$"{word} ", "abcdef", new string('x', perLine), "xxx"], Lines(h));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EnterStartsALineAndUpAndDownKeepTheColumn()
	{
		var (h, form) = Editing("abcdefgh");
		Tap(h, form, Key.Enter);
		h.Input.Type("12");
		h.Frame(form.Build);
		Tap(h, form, Key.Enter);
		h.Input.Type("ABCDEFG");
		h.Frame(form.Build);
		Assert.Equal("abcdefgh\n12\nABCDEFG", form.Notes);
		Assert.True(h.Ui.IsEditingText);

		// From column 7 of the last line: up to the short line (its end), up again back to column 7.
		Assert.Equal(19, h.Ui.EditingSelection.Caret);
		Tap(h, form, Key.Up);
		Assert.Equal(11, h.Ui.EditingSelection.Caret);
		Tap(h, form, Key.Up);
		Assert.Equal(7, h.Ui.EditingSelection.Caret);
		// Past the first line: the start of the text; past the last: its end.
		Tap(h, form, Key.Up);
		Assert.Equal(0, h.Ui.EditingSelection.Caret);
		Tap(h, form, Key.Down);
		Tap(h, form, Key.Down);
		Tap(h, form, Key.Down);
		Assert.Equal(form.Notes.Length, h.Ui.EditingSelection.Caret);

		// Shift+Up selects up a line.
		Tap(h, form, Key.Up, Shift);
		Assert.Equal((11, 11, 19), h.Ui.EditingSelection);

		// Escape stops editing; Enter does not.
		Tap(h, form, Key.Escape);
		Assert.False(h.Ui.IsEditingText);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void HomeAndEndGoToTheEndsOfTheLineAndWithCtrlOfTheText()
	{
		var (h, form) = Editing("first line\nsecond line");
		Tap(h, form, Key.Up);
		Tap(h, form, Key.End);
		Assert.Equal(10, h.Ui.EditingSelection.Caret);
		Tap(h, form, Key.Home);
		Assert.Equal(0, h.Ui.EditingSelection.Caret);
		Tap(h, form, Key.Down);
		Tap(h, form, Key.End);
		Assert.Equal(22, h.Ui.EditingSelection.Caret);
		Tap(h, form, Key.Home, Ctrl);
		Assert.Equal(0, h.Ui.EditingSelection.Caret);
		Tap(h, form, Key.End, Ctrl | Shift);
		Assert.Equal((22, 0, 22), h.Ui.EditingSelection);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheFieldScrollsToKeepTheCaretInViewAndTheWheelScrollsIt()
	{
		var (h, form) = Editing("", lines: 2);
		h.Input.Type("one");
		h.Frame(form.Build);
		foreach (var word in new[] { "two", "three", "four" })
		{
			Tap(h, form, Key.Enter);
			h.Input.Type(word);
			h.Frame(form.Build);
		}

		// Two lines shown: the caret's and the one above.
		Assert.Equal(["three", "four"], Lines(h));
		Tap(h, form, Key.Up);
		Tap(h, form, Key.Up);
		Assert.Equal(["two", "three"], Lines(h));
		Tap(h, form, Key.Home, Ctrl);
		Assert.Equal(["one", "two"], Lines(h));

		// The wheel over the field scrolls it, within the text.
		var rect = h.Node("Notes").Rect;
		h.Input.SetMousePosition(new Vector2(rect.CenterX, rect.CenterY));
		h.Input.Scroll(-1);
		h.Frame(form.Build);
		Assert.Equal(["two", "three"], Lines(h));
		h.Input.Scroll(-5);
		h.Frame(form.Build);
		Assert.Equal(["three", "four"], Lines(h));

		// The field is as tall as its lines.
		Assert.Equal(2 * 16 + 8, rect.Height);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ClicksPlaceTheCaretOnTheirLineAndPastesKeepLineBreaks()
	{
		var h = new UiHarness();
		var form = new Form { Notes = "abc\ndefgh" };
		h.Frame(form.Build);
		var top = h.Node("Notes").Rect.Y + 4;
		h.Input.SetMousePosition(new Vector2(TextX(h) + 2 * 8 + 1, top + 16 + 8));
		h.Input.Click();
		h.Frame(form.Build);
		Assert.True(h.Ui.IsEditingText);
		Assert.Equal(6, h.Ui.EditingSelection.Caret);   // "de|fgh" on the second line

		h.Ui.Clipboard.SetText("1\r\n2\t3");
		Tap(h, form, Key.V, Ctrl);
		Assert.Equal("abc\nde1\n2 3fgh", form.Notes);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheTreeSetsAndTypesTextAreas()
	{
		var h = new UiHarness();
		var form = new Form();
		h.Frame(form.Build);
		Assert.Equal(UiNodeKind.TextArea, h.Node("Notes").Kind);
		Assert.True(h.Tree.SetValue("Notes", "line 1\nline 2"));
		h.Frame(form.Build);
		Assert.Equal("line 1\nline 2", form.Notes);
		Assert.True(h.Tree.Type("Notes", "!"));
		h.Frame(form.Build);
		Assert.Equal("line 1\nline 2!", form.Notes);
		Assert.Equal(2, form.Changes);
	}
}
