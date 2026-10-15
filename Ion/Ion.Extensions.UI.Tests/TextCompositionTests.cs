using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.UI.Tests;

/// <summary>Input method composition in text inputs: shown at the caret, committed as text, and the caret reported to the platform.</summary>
public class TextCompositionTests
{
	private sealed class Form
	{
		public string Name = "";

		public void Build(Ui ui)
		{
			ui.TextInput("Name", ref Name, maxLength: 24);
			ui.Button("OK");
		}
	}

	/// <summary>A harness with a recording text input host, editing <paramref name="text"/> with the caret at its end.</summary>
	private static (UiHarness H, Form Form, RecordingTextInputHost Host) Editing(string text)
	{
		var host = new RecordingTextInputHost();
		var h = new UiHarness(textInput: host);
		var form = new Form { Name = text };
		h.Frame(form.Build);
		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		return (h, form, host);
	}

	private static float CaretX(UiHarness h, int index) => h.Node("Name").Rect.X + 32 + UiTheme.Default.Gap + 6 + index * 8;

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheCompositionIsShownAtTheCaretUnderlinedWithoutChangingTheValue()
	{
		var (h, form, _) = Editing("ab");
		h.Input.Tap(Key.Left);
		h.Frame(form.Build);
		h.Input.Compose("xyz", 1);
		h.Frame(form.Build);

		Assert.Equal("ab", form.Name);
		Assert.Equal("xyz", h.Ui.Composition);
		Assert.Contains(h.Batch.Draws, d => d.Text == "axyzb");
		// The underline spans the composition; the caret is at the composition's cursor.
		var underline = h.Batch.Draws.Single(d => d.Kind == "rect" && d.Rect.Height == 1);
		Assert.Equal(CaretX(h, 1), underline.Rect.X);
		Assert.Equal(3 * 8, underline.Rect.Width);
		var caret = h.Batch.Draws.Single(d => d.Kind == "rect" && d.Rect.Width == 2 && d.Color == UiTheme.Default.Accent);
		Assert.Equal(CaretX(h, 2), caret.Rect.X);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void KeysBelongToTheInputMethodWhileItComposesAndTheCommitIsInserted()
	{
		var (h, form, _) = Editing("ab");
		h.Input.Compose("ni");
		h.Frame(form.Build);

		// Backspace, arrows and Enter edit the composition, not the field.
		h.Input.Tap(Key.BackSpace);
		h.Input.Compose("n");
		h.Frame(form.Build);
		h.Input.Tap(Key.Left);
		h.Frame(form.Build);
		Assert.Equal("ab", form.Name);
		Assert.Equal(2, h.Ui.EditingSelection.Caret);

		// Enter commits: the text arrives, the composition ends, and editing goes on.
		h.Input.Tap(Key.Enter);
		h.Input.Type("日");
		h.Input.Compose("");
		h.Frame(form.Build);
		Assert.Equal("ab日", form.Name);
		Assert.True(h.Ui.IsEditingText);
		Assert.Equal("", h.Ui.Composition);

		// Afterwards keys edit the field again.
		h.Input.Tap(Key.BackSpace);
		h.Frame(form.Build);
		Assert.Equal("ab", form.Name);
		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		Assert.False(h.Ui.IsEditingText);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheCaretIsReportedToTheTextInputHostWhileEditing()
	{
		var host = new RecordingTextInputHost();
		var h = new UiHarness(textInput: host);
		var form = new Form { Name = "abc" };
		h.Frame(form.Build);
		Assert.Null(host.Area);

		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		var area = Assert.NotNull(host.Area);
		var field = h.Node("Name").Rect;
		Assert.Equal(CaretX(h, 3), area.X);
		Assert.Equal(16, area.Height);
		Assert.InRange(area.Y, field.Y, field.Y + field.Height);

		// The caret moves with a composition's cursor.
		h.Input.Compose("de", 1);
		h.Frame(form.Build);
		Assert.Equal(CaretX(h, 4), host.Area!.Value.X);

		// Leaving the field clears it.
		h.Input.Compose("");
		h.Frame(form.Build);
		h.Frame(form.Build);
		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		Assert.False(h.Ui.IsEditingText);
		Assert.Null(host.Area);
	}
}
