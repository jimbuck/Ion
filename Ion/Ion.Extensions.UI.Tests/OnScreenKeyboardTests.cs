using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.UI.Tests;

/// <summary>The built-in on-screen keyboard: when it opens, gamepad typing and navigation, pointer keys, and the platform's keyboard taking over.</summary>
public class OnScreenKeyboardTests
{
	private sealed class Form
	{
		public string Name = "";

		public void Build(Ui ui)
		{
			ui.TextInput("Name", ref Name, maxLength: 16);
			ui.Button("OK");
		}
	}

	/// <summary>A text input host that, like a phone, shows its own keyboard.</summary>
	private sealed class PhoneHost : ITextInputHost
	{
		public void SetTextInputArea(float x, float y, float width, float height) { }

		public void ClearTextInputArea() { }

		public bool ShowsScreenKeyboard => true;
	}

	private static (UiHarness H, Form Form) WithGamepad(UiOptions? options = null, ITextInputHost? host = null)
	{
		var h = new UiHarness(options: options, textInput: host);
		var form = new Form();
		h.Input.ConnectGamepad(0);
		h.Frame(form.Build);
		return (h, form);
	}

	private static void Pad(UiHarness h, Form form, GamepadButton button)
	{
		h.Input.Tap(0, button);
		h.Frame(form.Build);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EditingFromAGamepadOpensTheKeyboardWhichTypesDeletesAndFinishes()
	{
		var (h, form) = WithGamepad();
		Pad(h, form, GamepadButton.A);
		Assert.True(h.Ui.IsEditingText);
		Assert.True(h.Ui.IsKeyboardOpen);
		Assert.Equal((1, 0, false), h.Ui.KeyboardSelection);   // "q"

		Pad(h, form, GamepadButton.A);
		Pad(h, form, GamepadButton.DPadRight);
		Pad(h, form, GamepadButton.Y);                          // Shift, for one character
		Pad(h, form, GamepadButton.A);
		Pad(h, form, GamepadButton.A);
		Assert.Equal("qWw", form.Name);

		Pad(h, form, GamepadButton.X);
		Pad(h, form, GamepadButton.B);
		Pad(h, form, GamepadButton.B);
		Assert.Equal("qW", form.Name);
		Pad(h, form, GamepadButton.LeftShoulder);
		Pad(h, form, GamepadButton.DPadUp);                     // row 0: "2"
		Pad(h, form, GamepadButton.A);
		Assert.Equal("q2W", form.Name);

		// Start finishes, without the same press reaching the focused field.
		Pad(h, form, GamepadButton.Start);
		Assert.False(h.Ui.IsEditingText);
		Assert.False(h.Ui.IsKeyboardOpen);
		Assert.Equal("Name", h.Tree.FocusedPath);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheSelectionWrapsAndMovesBetweenTheCharacterRowsAndTheSpecialKeys()
	{
		var (h, form) = WithGamepad();
		Pad(h, form, GamepadButton.A);

		// Left from the first key wraps to the last of the row.
		Pad(h, form, GamepadButton.DPadLeft);
		Assert.Equal((1, 9, false), h.Ui.KeyboardSelection);

		// Down twice to the bottom character row, once more to the special row: the last unit is Done.
		Pad(h, form, GamepadButton.DPadDown);
		Pad(h, form, GamepadButton.DPadDown);
		Pad(h, form, GamepadButton.DPadDown);
		Assert.Equal((4, 3, false), h.Ui.KeyboardSelection);
		Pad(h, form, GamepadButton.DPadLeft);
		Pad(h, form, GamepadButton.DPadLeft);
		Assert.Equal((4, 1, false), h.Ui.KeyboardSelection);   // Space

		// Space covers units 2 to 5: up lands on unit 2; down from the special row wraps to the top row.
		Pad(h, form, GamepadButton.DPadUp);
		Assert.Equal((3, 2, false), h.Ui.KeyboardSelection);
		Pad(h, form, GamepadButton.DPadDown);
		Pad(h, form, GamepadButton.DPadDown);
		Assert.Equal((0, 2, false), h.Ui.KeyboardSelection);

		// The left stick moves too.
		h.Input.SetLeftStick(0, new Vector2(0.9f, 0));
		h.Frame(form.Build);
		h.Input.SetLeftStick(0, Vector2.Zero);
		h.Frame(form.Build);
		Assert.Equal((0, 3, false), h.Ui.KeyboardSelection);

		// The Space and Done keys.
		Pad(h, form, GamepadButton.A);
		Pad(h, form, GamepadButton.DPadUp);                     // up from the top row wraps to the special row: Space
		Assert.Equal((4, 1, false), h.Ui.KeyboardSelection);
		Pad(h, form, GamepadButton.A);
		Assert.Equal("4 ", form.Name);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void KeysCanBeClickedOrTappedAndTheyAreDrawnOverTheUi()
	{
		var (h, form) = WithGamepad();
		Pad(h, form, GamepadButton.A);
		var q = h.Batch.Draws.Single(d => d.Text == "q");
		var w = h.Batch.Draws.Single(d => d.Text == "w");
		Assert.True(w.Rect.X > q.Rect.X);
		// At the bottom of the viewport, after everything else.
		Assert.True(q.Rect.Y > h.Node("OK").Rect.Y);
		Assert.True(h.Batch.Draws.FindIndex(d => d.Text == "q") > h.Batch.Draws.FindIndex(d => d.Text == "OK"));

		h.Input.SetMousePosition(new Vector2(w.Rect.X + 2, w.Rect.Y + 2));
		h.Input.Click();
		h.Frame(form.Build);
		Assert.Equal("w", form.Name);
		Assert.True(h.Ui.IsEditingText);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheModeAndThePlatformDecideWhetherItOpens()
	{
		// Auto: a keyboard or mouse start does not open it.
		var (h, form) = WithGamepad();
		h.Input.Tap(Key.Enter);
		h.Frame(form.Build);
		Assert.True(h.Ui.IsEditingText);
		Assert.False(h.Ui.IsKeyboardOpen);

		// Always: it opens for a click too.
		var (always, alwaysForm) = WithGamepad(new UiOptions { OnScreenKeyboard = OnScreenKeyboardMode.Always });
		always.ClickAt("Name");
		always.Frame(alwaysForm.Build);
		Assert.True(always.Ui.IsKeyboardOpen);

		// Never, or a platform that shows its own keyboard (a phone): it does not.
		var (never, neverForm) = WithGamepad(new UiOptions { OnScreenKeyboard = OnScreenKeyboardMode.Never });
		Pad(never, neverForm, GamepadButton.A);
		Assert.True(never.Ui.IsEditingText);
		Assert.False(never.Ui.IsKeyboardOpen);

		var (phone, phoneForm) = WithGamepad(new UiOptions { OnScreenKeyboard = OnScreenKeyboardMode.Always }, new PhoneHost());
		Pad(phone, phoneForm, GamepadButton.A);
		Assert.True(phone.Ui.IsEditingText);
		Assert.False(phone.Ui.IsKeyboardOpen);
	}
}
