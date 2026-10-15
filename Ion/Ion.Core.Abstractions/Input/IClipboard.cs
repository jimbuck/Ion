namespace Ion;

/// <summary>
/// The system clipboard's text: what copy, cut and paste exchange with other programs. The window backends provide it
/// (the desktop clipboard through Silk.NET); headless and tests get a <see cref="MemoryClipboard"/>.
/// </summary>
public interface IClipboard
{
	/// <summary>The clipboard's text, or an empty string when it holds none.</summary>
	string GetText();

	/// <summary>Replaces the clipboard's contents with <paramref name="text"/>.</summary>
	void SetText(string text);
}

/// <summary>A clipboard that keeps its text in memory (headless runs, tests, platforms without a system clipboard).</summary>
public sealed class MemoryClipboard : IClipboard
{
	/// <summary>The text held.</summary>
	public string Text { get; set; } = string.Empty;

	/// <inheritdoc/>
	public string GetText() => Text;

	/// <inheritdoc/>
	public void SetText(string text) => Text = text ?? string.Empty;
}
