using System.Text;

using Ion.Extensions.Windowing;

namespace Ion.Extensions.Graphics.Vulkan.Tests;

/// <summary>The Silk.NET module's decoding of SDL input method compositions, without a window or an IME.</summary>
public class SilkTextInputTests
{
	private static byte[] Field(string text)
	{
		// SDL's 32-byte, null-terminated UTF-8 field.
		var field = new byte[32];
		var bytes = Encoding.UTF8.GetBytes(text);
		bytes.AsSpan(0, Math.Min(bytes.Length, 31)).CopyTo(field);
		return field;
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CompositionsDecodeFromUtf8WithTheCursorInUtf16Positions()
	{
		// Three hiragana (nine UTF-8 bytes) with the cursor after the second.
		Assert.Equal(InputEvent.ForComposition("にほん", 2), SdlTextInput.Map(Field("にほん"), 2));

		// A character outside the BMP is two UTF-16 units: a cursor after it is at 2.
		Assert.Equal(InputEvent.ForComposition("\U00020BB7a", 2), SdlTextInput.Map(Field("\U00020BB7a"), 1));

		// An empty field ends the composition.
		Assert.Equal(InputEvent.ForComposition("", 0), SdlTextInput.Map(new byte[32], 0));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ACompositionCutInsideACharacterDropsTheBrokenTail()
	{
		// Eleven three-byte characters do not fit 31 bytes: the field ends one byte into the eleventh.
		var text = new string('一', 11);
		var field = new byte[32];
		Encoding.UTF8.GetBytes(text).AsSpan(0, 31).CopyTo(field);
		var composition = SdlTextInput.Map(field, 0);
		Assert.Equal(new string('一', 10), composition.Text);
	}
}
