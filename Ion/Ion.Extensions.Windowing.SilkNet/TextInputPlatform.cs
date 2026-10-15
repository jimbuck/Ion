using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Silk.NET.Maths;
using Silk.NET.SDL;

namespace Ion.Extensions.Windowing;

/// <summary>
/// Receives SDL's input method composition events (<c>SDL_TEXTEDITING</c>) and turns them into Ion composition events,
/// with an event watch like <see cref="SdlTouchSource"/> (Silk.NET.Input has no composition events), and places SDL's
/// candidate window with <c>SDL_SetTextInputRect</c>.
/// </summary>
/// <remarks>
/// SDL carries a composition in a 32-byte UTF-8 field, so a composition longer than that (about ten CJK characters) is
/// cut; the committed text is not (it arrives as text input). The composition's cursor is counted in code points and is
/// converted to UTF-16 positions.
/// </remarks>
internal sealed unsafe class SdlTextInput : IDisposable
{
	private readonly Sdl _sdl;
	private readonly Action<InputEvent> _sink;
	private readonly Lock _lock = new();
	private GCHandle _self;

	/// <summary>Adds the event watch. <paramref name="sink"/> is called under this source's lock, from SDL's event thread.</summary>
	public SdlTextInput(Sdl sdl, Action<InputEvent> sink)
	{
		_sdl = sdl;
		_sink = sink;
		_self = GCHandle.Alloc(this);
		_sdl.AddEventWatch(new PfnEventFilter(&OnEvent), (void*)GCHandle.ToIntPtr(_self));
	}

	/// <summary>Removes the event watch.</summary>
	public void Dispose()
	{
		if (!_self.IsAllocated) return;
		_sdl.DelEventWatch(new PfnEventFilter(&OnEvent), (void*)GCHandle.ToIntPtr(_self));
		_self.Free();
	}

	/// <summary>Starts text input (SDL starts it by default on desktop; mobile shows the on-screen keyboard) and places the candidate window at the caret.</summary>
	public void SetArea(int x, int y, int width, int height)
	{
		var rect = new Rectangle<int>(x, y, Math.Max(1, width), Math.Max(1, height));
		_sdl.SetTextInputRect(&rect);
		if (_sdl.IsTextInputActive() == SdlBool.False) _sdl.StartTextInput();
	}

	/// <summary>Whether the platform has an on-screen keyboard that text input shows (<c>SDL_HasScreenKeyboardSupport</c>).</summary>
	public bool HasScreenKeyboard => _sdl.HasScreenKeyboardSupport() == SdlBool.True;

	/// <summary>Stops text input (hides a phone's keyboard).</summary>
	public void Stop()
	{
		if (_sdl.IsTextInputActive() == SdlBool.True) _sdl.StopTextInput();
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int OnEvent(void* userdata, Event* e)
	{
		try
		{
			if (e->Type != (uint)EventType.Textediting) return 1;
			if (GCHandle.FromIntPtr((nint)userdata).Target is not SdlTextInput source) return 1;
			var edit = e->Edit;
			var composition = Map(new ReadOnlySpan<byte>(edit.Text, 32), edit.Start);
			lock (source._lock) source._sink(composition);
		}
		catch
		{
			// Never let an exception unwind into SDL.
		}

		return 1;
	}

	/// <summary>
	/// One SDL composition: the null-terminated UTF-8 <paramref name="utf8"/> and the cursor <paramref name="start"/> (in
	/// code points), as an Ion composition event (cursor in UTF-16 positions).
	/// </summary>
	internal static InputEvent Map(ReadOnlySpan<byte> utf8, int start)
	{
		var end = utf8.IndexOf((byte)0);
		if (end >= 0) utf8 = utf8[..end];
		var text = Encoding.UTF8.GetString(utf8);
		// A cut composition may end inside a multi-byte character, which decodes to U+FFFD: drop it.
		if (text.EndsWith('�')) text = text[..^1];
		var cursor = 0;
		var runes = 0;
		foreach (var rune in text.EnumerateRunes())
		{
			if (runes++ >= start) break;
			cursor += rune.Utf16SequenceLength;
		}

		return InputEvent.ForComposition(text, cursor);
	}
}

/// <summary>
/// Places the Windows input method's composition and candidate windows at the caret for a GLFW window (GLFW reports no
/// composition: Windows draws the composition string itself, at the given position, and commits the text as characters).
/// </summary>
internal static partial class Win32Ime
{
	private const uint CfsPoint = 0x0002;
	private const uint CfsCandidatePos = 0x0040;

	/// <summary>Moves the composition window to (<paramref name="x"/>, <paramref name="y"/>) and the candidate list under the caret (client pixels).</summary>
	public static void SetArea(nint hwnd, int x, int y, int height)
	{
		if (!OperatingSystem.IsWindows() || hwnd == 0) return;
		var context = ImmGetContext(hwnd);
		if (context == 0) return;
		try
		{
			var composition = new CompositionForm { Style = CfsPoint, X = x, Y = y };
			ImmSetCompositionWindow(context, ref composition);
			var candidate = new CandidateForm { Index = 0, Style = CfsCandidatePos, X = x, Y = y + height };
			ImmSetCandidateWindow(context, ref candidate);
		}
		finally
		{
			ImmReleaseContext(hwnd, context);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct CompositionForm
	{
		public uint Style;
		public int X, Y;
		public int Left, Top, Right, Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct CandidateForm
	{
		public uint Index;
		public uint Style;
		public int X, Y;
		public int Left, Top, Right, Bottom;
	}

	[LibraryImport("imm32.dll")]
	private static partial nint ImmGetContext(nint hwnd);

	[LibraryImport("imm32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool ImmReleaseContext(nint hwnd, nint context);

	[LibraryImport("imm32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool ImmSetCompositionWindow(nint context, ref CompositionForm form);

	[LibraryImport("imm32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool ImmSetCandidateWindow(nint context, ref CandidateForm form);
}
