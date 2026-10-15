using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.UI.Tests;

/// <summary>Text input selection (keyboard and pointer) and the clipboard operations.</summary>
public class TextSelectionTests
{
	private const ModifierKeys Shift = ModifierKeys.Shift;
	private const ModifierKeys Ctrl = ModifierKeys.Control;

	private sealed class Form
	{
		public string Name = "";

		public void Build(Ui ui)
		{
			ui.TextInput("Name", ref Name, maxLength: 24);
			ui.Button("OK");
		}
	}

	/// <summary>A harness editing <paramref name="text"/> with the caret at its end.</summary>
	private static (UiHarness H, Form Form) Editing(string text)
	{
		var h = new UiHarness();
		var form = new Form { Name = text };
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

	/// <summary>The x of the caret position <paramref name="index"/> in the field (label 32 px, gap, 6 px padding, 8 px per character).</summary>
	private static float CaretX(UiHarness h, int index) => h.Node("Name").Rect.X + 32 + UiTheme.Default.Gap + 6 + index * 8;

	private static void PressAt(UiHarness h, Form form, float x)
	{
		var rect = h.Node("Name").Rect;
		h.Input.SetMousePosition(new Vector2(x, rect.CenterY));
		h.Input.Click();
		h.Frame(form.Build);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ShiftArrowsSelectAndTypingReplacesTheSelection()
	{
		var (h, form) = Editing("hello world");
		Tap(h, form, Key.Left, Shift);
		Tap(h, form, Key.Left, Shift);
		Assert.Equal((9, 9, 11), h.Ui.EditingSelection);

		h.Input.Type("X");
		h.Frame(form.Build);
		Assert.Equal("hello worX", form.Name);

		// Shift+Home selects to the start; a plain arrow drops the selection at its edge.
		Tap(h, form, Key.Home, Shift);
		Assert.Equal((0, 0, 10), h.Ui.EditingSelection);
		Tap(h, form, Key.Right);
		Assert.Equal((10, 10, 10), h.Ui.EditingSelection);
		Tap(h, form, Key.Home, Shift);
		Tap(h, form, Key.Left);
		Assert.Equal((0, 0, 0), h.Ui.EditingSelection);

		// Backspace and Delete remove the selection whole.
		Tap(h, form, Key.End, Shift);
		Tap(h, form, Key.Delete);
		Assert.Equal("", form.Name);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CtrlArrowsMoveByWordsAndCtrlBackspaceDeletesAWord()
	{
		var (h, form) = Editing("one two, three");
		Tap(h, form, Key.Left, Ctrl);
		Assert.Equal(9, h.Ui.EditingSelection.Caret);    // the start of "three"
		Tap(h, form, Key.Left, Ctrl);
		Assert.Equal(7, h.Ui.EditingSelection.Caret);    // the comma
		Tap(h, form, Key.Left, Ctrl);
		Assert.Equal(4, h.Ui.EditingSelection.Caret);    // the start of "two"
		Tap(h, form, Key.Right, Ctrl | Shift);
		Assert.Equal((7, 4, 7), h.Ui.EditingSelection);  // "two" selected

		Tap(h, form, Key.End);
		Tap(h, form, Key.BackSpace, Ctrl);
		Assert.Equal("one two, ", form.Name);
		Tap(h, form, Key.Home);
		Tap(h, form, Key.Delete, Ctrl);
		Assert.Equal(" two, ", form.Name);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SelectAllCopyCutAndPaste()
	{
		var (h, form) = Editing("Ada Lovelace");
		Tap(h, form, Key.A, Ctrl);
		Assert.Equal((12, 0, 12), h.Ui.EditingSelection);
		Tap(h, form, Key.C, Ctrl);
		Assert.Equal("Ada Lovelace", h.Ui.Clipboard.GetText());
		Assert.Equal("Ada Lovelace", form.Name);

		// Cut the surname.
		Tap(h, form, Key.End);
		Tap(h, form, Key.Left, Ctrl | Shift);
		Tap(h, form, Key.X, Ctrl);
		Assert.Equal("Lovelace", h.Ui.Clipboard.GetText());
		Assert.Equal("Ada ", form.Name);

		// Paste it back twice; a paste replaces the selection, and line breaks become spaces.
		Tap(h, form, Key.V, Ctrl);
		Tap(h, form, Key.V, Ctrl);
		Assert.Equal("Ada LovelaceLovelace", form.Name);
		Tap(h, form, Key.Left, Ctrl | Shift);
		h.Ui.Clipboard.SetText("King\r\nof\tthe\nhill");
		Tap(h, form, Key.V, Ctrl);
		Assert.Equal("Ada King of the hill", form.Name);

		// The field's length limit cuts a long paste.
		h.Ui.Clipboard.SetText(new string('x', 40));
		Tap(h, form, Key.V, Ctrl);
		Assert.Equal(24, form.Name.Length);

		// Copying or cutting nothing leaves the clipboard alone; a command key held does not type its letter.
		h.Ui.Clipboard.SetText("kept");
		Tap(h, form, Key.End);
		Tap(h, form, Key.C, Ctrl);
		Tap(h, form, Key.X, Ctrl);
		Assert.Equal("kept", h.Ui.Clipboard.GetText());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ClickingPutsTheCaretAtThePointerAndDraggingSelects()
	{
		var h = new UiHarness();
		var form = new Form { Name = "abcdefgh" };
		h.Frame(form.Build);

		// A click a little right of the boundary between "c" and "d" puts the caret there (the nearest boundary).
		PressAt(h, form, CaretX(h, 3) + 2);
		Assert.True(h.Ui.IsEditingText);
		Assert.Equal((3, 3, 3), h.Ui.EditingSelection);

		// Press at 1 and drag to 6: "bcdef" selected (after the double-click time, so it is a new single click).
		h.Frames(30, form.Build);
		var rect = h.Node("Name").Rect;
		h.Input.SetMousePosition(new Vector2(CaretX(h, 1), rect.CenterY));
		h.Input.Press(MouseButton.Left);
		h.Frame(form.Build);
		h.Input.SetMousePosition(new Vector2(CaretX(h, 6) - 1, rect.CenterY));
		h.Frame(form.Build);
		h.Input.Release(MouseButton.Left);
		h.Frame(form.Build);
		Assert.Equal((6, 1, 6), h.Ui.EditingSelection);

		// Past the end of the text: the end.
		h.Frames(30, form.Build);
		PressAt(h, form, CaretX(h, 20));
		Assert.Equal((8, 8, 8), h.Ui.EditingSelection);

		// Shift-click extends from the caret.
		h.Frames(30, form.Build);
		h.Input.Press(Key.ShiftLeft);
		h.Frame(form.Build);
		PressAt(h, form, CaretX(h, 2));
		h.Input.Release(Key.ShiftLeft);
		h.Frame(form.Build);
		Assert.Equal((2, 2, 8), h.Ui.EditingSelection);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ADoubleClickSelectsAWordAndATripleClickEverything()
	{
		var h = new UiHarness();
		var form = new Form { Name = "grace hopper" };
		h.Frame(form.Build);
		PressAt(h, form, CaretX(h, 8));
		PressAt(h, form, CaretX(h, 8));
		Assert.Equal((12, 6, 12), h.Ui.EditingSelection);
		PressAt(h, form, CaretX(h, 8));
		Assert.Equal((12, 0, 12), h.Ui.EditingSelection);

		// Slow clicks are single clicks.
		h.Frames(60, form.Build);
		PressAt(h, form, CaretX(h, 2));
		h.Frames(60, form.Build);
		PressAt(h, form, CaretX(h, 2));
		Assert.Equal((2, 2, 2), h.Ui.EditingSelection);

		// At the end of a word, a double click picks the word, not the space after it.
		h.Frames(60, form.Build);
		PressAt(h, form, CaretX(h, 5));
		PressAt(h, form, CaretX(h, 5));
		Assert.Equal((5, 0, 5), h.Ui.EditingSelection);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheSelectionIsDrawnBehindTheText()
	{
		var (h, form) = Editing("abcdef");
		Tap(h, form, Key.Left);
		Tap(h, form, Key.Left, Shift);
		Tap(h, form, Key.Left, Shift);
		Tap(h, form, Key.Left, Shift);
		var highlight = h.Batch.Draws.Single(d => d.Kind == "rect" && d.Color == UiTheme.Default.TextSelection);
		Assert.Equal(CaretX(h, 2), highlight.Rect.X);
		Assert.Equal(3 * 8, highlight.Rect.Width);
		// It comes before the text, so the text stays readable.
		var text = h.Batch.Draws.FindIndex(d => d.Text == "abcdef");
		Assert.True(h.Batch.Draws.IndexOf(highlight) < text);

		// Leaving the field hides it.
		Tap(h, form, Key.Enter);
		Assert.DoesNotContain(h.Batch.Draws, d => d.Color == UiTheme.Default.TextSelection);
	}
}
