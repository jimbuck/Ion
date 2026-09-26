using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.UI.Tests;

public class TextInputTests
{
	private sealed class Form
	{
		public string Name = "";
		public int Changes;

		public void Build(Ui ui)
		{
			if (ui.TextInput("Name", ref Name, maxLength: 8)) Changes++;
			ui.Button("OK");
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ClickingStartsEditingAndTypedTextIsInsertedAtTheCaret()
	{
		var h = new UiHarness();
		var form = new Form();
		h.Frame(form.Build);
		h.ClickAt("Name");
		h.Frame(form.Build);
		Assert.True(h.Ui.IsEditingText);

		h.Input.Type("abc");
		h.Frame(form.Build);
		Assert.Equal("abc", form.Name);
		Assert.Equal("abc", h.Node("Name").Value);

		h.Input.Tap(Key.BackSpace);
		h.Frame(form.Build);
		Assert.Equal("ab", form.Name);

		h.Input.Tap(Key.Left);
		h.Frame(form.Build);
		h.Input.Type("X");
		h.Frame(form.Build);
		Assert.Equal("aXb", form.Name);

		h.Input.Tap(Key.Home);
		h.Frame(form.Build);
		h.Input.Type("0");
		h.Frame(form.Build);
		h.Input.Tap(Key.Delete);
		h.Frame(form.Build);
		Assert.Equal("0Xb", form.Name);

		h.Input.Tap(Key.End);
		h.Frame(form.Build);
		h.Input.Type("123456789");
		h.Frame(form.Build);
		Assert.Equal("0Xb12345", form.Name);

		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		Assert.False(h.Ui.IsEditingText);
		Assert.Equal("Name", h.Tree.FocusedPath);

		// Not editing: typing goes nowhere, and Enter starts editing again.
		h.Input.Type("zz");
		h.Frame(form.Build);
		Assert.Equal("0Xb12345", form.Name);
		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		Assert.True(h.Ui.IsEditingText);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheCaretIsDrawnAfterTheTextBeforeIt()
	{
		var h = new UiHarness();
		var form = new Form { Name = "hello" };
		h.Frame(form.Build);
		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		h.Input.Tap(Key.Left);
		h.Frame(form.Build);
		h.Input.Tap(Key.Left);
		h.Frame(form.Build);

		var field = h.Node("Name").Rect;
		var caret = h.Batch.Draws.Single(d => d.Kind == "rect" && d.Rect.Width == 2 && d.Color == UiTheme.Default.Accent);
		// Label "Name" (32 px), gap, 6 px of text padding, then 3 characters of 8 px.
		Assert.Equal(field.X + 32 + UiTheme.Default.Gap + 6 + 24, caret.Rect.X);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EscapeAndGamepadBStopEditingWithoutReportingBack()
	{
		var h = new UiHarness();
		var form = new Form();
		h.Input.ConnectGamepad(0);
		h.Frame(form.Build);
		h.Input.Tap(0, GamepadButton.A);
		h.Frame(form.Build);
		Assert.True(h.Ui.IsEditingText);
		h.Input.Tap(0, GamepadButton.B);
		h.Frame(form.Build);
		Assert.False(h.Ui.IsEditingText);
		Assert.False(h.Ui.BackPressed);

		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		h.Input.Tap(Key.Escape);
		h.Frame(form.Build);
		Assert.False(h.Ui.IsEditingText);
		Assert.False(h.Ui.BackPressed);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TabAndArrowsLeaveTheField()
	{
		var h = new UiHarness();
		var form = new Form();
		h.Frame(form.Build);
		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		h.Input.Tap(Key.Down);
		h.Frame(form.Build);
		Assert.False(h.Ui.IsEditingText);
		Assert.Equal("OK", h.Tree.FocusedPath);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TreeSetValueReplacesAndTypeInserts()
	{
		var h = new UiHarness();
		var form = new Form();
		h.Frame(form.Build);
		Assert.True(h.Tree.SetValue("Name", "Ada Lovelace"));
		h.Frame(form.Build);
		Assert.Equal("Ada Love", form.Name);
		Assert.Equal(1, form.Changes);

		Assert.True(h.Tree.SetValue("Name", "Ada"));
		h.Frame(form.Build);
		Assert.True(h.Tree.Type("Name", "m"));
		h.Frame(form.Build);
		Assert.Equal("Adam", form.Name);
		Assert.True(h.Ui.IsEditingText);
		Assert.Equal(3, form.Changes);

		Assert.False(h.Tree.Type("OK", "x"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AnExternalChangeOfTheValueIsPickedUp()
	{
		var h = new UiHarness();
		var form = new Form();
		h.Frame(form.Build);
		form.Name = "Grace";
		h.Frame(form.Build);
		Assert.Equal("Grace", h.Node("Name").Value);
		h.Tree.Type("Name", "!");
		h.Frame(form.Build);
		Assert.Equal("Grace!", form.Name);
	}
}
