namespace Ion;

/// <summary>
/// Tells the platform where text is being edited, so the input method (IME) can place its candidate and composition
/// windows next to the caret (and, on touch devices, so an on-screen keyboard can be shown). The UI module calls it every
/// frame a text field is being edited, with the caret's rectangle in window coordinates, and with null once editing
/// stops. The window backends implement it; elsewhere it does nothing.
/// </summary>
public interface ITextInputHost
{
	/// <summary>Text is being edited with the caret at (<paramref name="x"/>, <paramref name="y"/>), <paramref name="width"/> by <paramref name="height"/> (window coordinates).</summary>
	void SetTextInputArea(float x, float y, float width, float height);

	/// <summary>No text is being edited any more.</summary>
	void ClearTextInputArea();

	/// <summary>
	/// Whether the platform shows its own on-screen keyboard while text is edited (a phone's), so the UI does not show its
	/// built-in one. False where there is none (desktops, handhelds without touch).
	/// </summary>
	bool ShowsScreenKeyboard => false;
}

/// <summary>A text input host that records the last area (headless runs and tests).</summary>
public sealed class RecordingTextInputHost : ITextInputHost
{
	/// <summary>The caret rectangle last set (x, y, width, height), or null when cleared.</summary>
	public (float X, float Y, float Width, float Height)? Area { get; private set; }

	/// <summary>How many times an area was set.</summary>
	public int Updates { get; private set; }

	/// <inheritdoc/>
	public void SetTextInputArea(float x, float y, float width, float height)
	{
		Area = (x, y, width, height);
		Updates++;
	}

	/// <inheritdoc/>
	public void ClearTextInputArea() => Area = null;
}
