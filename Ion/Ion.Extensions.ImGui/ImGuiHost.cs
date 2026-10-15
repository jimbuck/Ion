using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

using Hexa.NET.ImGui;

using Microsoft.Extensions.Options;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

using DearImGui = Hexa.NET.ImGui.ImGui;

namespace Ion.Extensions.ImGui;

/// <summary>
/// The application's Dear ImGui context, fed from Ion's input and clock and drawn on the RHI by <c>UseImGui()</c>'s
/// system. Between the First stage and the end of Render, a frame is open: call <c>ImGui.*</c> from any step of
/// FixedUpdate, Update or Render (on the game thread). Read <see cref="WantCaptureMouse"/> and
/// <see cref="WantCaptureKeyboard"/> to keep the game from reacting to input meant for ImGui.
/// </summary>
/// <remarks>
/// Dear ImGui's current context is process-wide: the module makes this one current at the start of each of its steps,
/// so several applications in one process (tests) work as long as their frames do not interleave.
/// </remarks>
public sealed unsafe class ImGuiHost : IDisposable
{
	private static readonly (Key Key, ImGuiKey ImGui)[] Keys =
	[
		(Key.Tab, ImGuiKey.Tab), (Key.Left, ImGuiKey.LeftArrow), (Key.Right, ImGuiKey.RightArrow), (Key.Up, ImGuiKey.UpArrow),
		(Key.Down, ImGuiKey.DownArrow), (Key.PageUp, ImGuiKey.PageUp), (Key.PageDown, ImGuiKey.PageDown), (Key.Home, ImGuiKey.Home),
		(Key.End, ImGuiKey.End), (Key.Insert, ImGuiKey.Insert), (Key.Delete, ImGuiKey.Delete), (Key.BackSpace, ImGuiKey.Backspace),
		(Key.Space, ImGuiKey.Space), (Key.Enter, ImGuiKey.Enter), (Key.Escape, ImGuiKey.Escape),
		(Key.ControlLeft, ImGuiKey.LeftCtrl), (Key.ShiftLeft, ImGuiKey.LeftShift), (Key.AltLeft, ImGuiKey.LeftAlt), (Key.WinLeft, ImGuiKey.LeftSuper),
		(Key.ControlRight, ImGuiKey.RightCtrl), (Key.ShiftRight, ImGuiKey.RightShift), (Key.AltRight, ImGuiKey.RightAlt), (Key.WinRight, ImGuiKey.RightSuper),
		(Key.Menu, ImGuiKey.Menu),
		(Key.Number0, ImGuiKey.Key0), (Key.Number1, ImGuiKey.Key1), (Key.Number2, ImGuiKey.Key2), (Key.Number3, ImGuiKey.Key3), (Key.Number4, ImGuiKey.Key4),
		(Key.Number5, ImGuiKey.Key5), (Key.Number6, ImGuiKey.Key6), (Key.Number7, ImGuiKey.Key7), (Key.Number8, ImGuiKey.Key8), (Key.Number9, ImGuiKey.Key9),
		(Key.A, ImGuiKey.A), (Key.B, ImGuiKey.B), (Key.C, ImGuiKey.C), (Key.D, ImGuiKey.D), (Key.E, ImGuiKey.E), (Key.F, ImGuiKey.F), (Key.G, ImGuiKey.G),
		(Key.H, ImGuiKey.H), (Key.I, ImGuiKey.I), (Key.J, ImGuiKey.J), (Key.K, ImGuiKey.K), (Key.L, ImGuiKey.L), (Key.M, ImGuiKey.M), (Key.N, ImGuiKey.N),
		(Key.O, ImGuiKey.O), (Key.P, ImGuiKey.P), (Key.Q, ImGuiKey.Q), (Key.R, ImGuiKey.R), (Key.S, ImGuiKey.S), (Key.T, ImGuiKey.T), (Key.U, ImGuiKey.U),
		(Key.V, ImGuiKey.V), (Key.W, ImGuiKey.W), (Key.X, ImGuiKey.X), (Key.Y, ImGuiKey.Y), (Key.Z, ImGuiKey.Z),
		(Key.F1, ImGuiKey.F1), (Key.F2, ImGuiKey.F2), (Key.F3, ImGuiKey.F3), (Key.F4, ImGuiKey.F4), (Key.F5, ImGuiKey.F5), (Key.F6, ImGuiKey.F6),
		(Key.F7, ImGuiKey.F7), (Key.F8, ImGuiKey.F8), (Key.F9, ImGuiKey.F9), (Key.F10, ImGuiKey.F10), (Key.F11, ImGuiKey.F11), (Key.F12, ImGuiKey.F12),
		(Key.Quote, ImGuiKey.Apostrophe), (Key.Comma, ImGuiKey.Comma), (Key.Minus, ImGuiKey.Minus), (Key.Period, ImGuiKey.Period),
		(Key.Slash, ImGuiKey.Slash), (Key.Semicolon, ImGuiKey.Semicolon), (Key.Plus, ImGuiKey.Equal), (Key.BracketLeft, ImGuiKey.LeftBracket),
		(Key.BackSlash, ImGuiKey.Backslash), (Key.BracketRight, ImGuiKey.RightBracket), (Key.Grave, ImGuiKey.GraveAccent),
		(Key.CapsLock, ImGuiKey.CapsLock), (Key.ScrollLock, ImGuiKey.ScrollLock), (Key.NumLock, ImGuiKey.NumLock),
		(Key.PrintScreen, ImGuiKey.PrintScreen), (Key.Pause, ImGuiKey.Pause), (Key.NonUSBackSlash, ImGuiKey.Oem102),
		(Key.Keypad0, ImGuiKey.Keypad0), (Key.Keypad1, ImGuiKey.Keypad1), (Key.Keypad2, ImGuiKey.Keypad2), (Key.Keypad3, ImGuiKey.Keypad3),
		(Key.Keypad4, ImGuiKey.Keypad4), (Key.Keypad5, ImGuiKey.Keypad5), (Key.Keypad6, ImGuiKey.Keypad6), (Key.Keypad7, ImGuiKey.Keypad7),
		(Key.Keypad8, ImGuiKey.Keypad8), (Key.Keypad9, ImGuiKey.Keypad9), (Key.KeypadDecimal, ImGuiKey.KeypadDecimal),
		(Key.KeypadDivide, ImGuiKey.KeypadDivide), (Key.KeypadMultiply, ImGuiKey.KeypadMultiply), (Key.KeypadSubtract, ImGuiKey.KeypadSubtract),
		(Key.KeypadAdd, ImGuiKey.KeypadAdd), (Key.KeypadEnter, ImGuiKey.KeypadEnter),
	];

	// ImGui's mouse button indices: 0 left, 1 right, 2 middle, then the extra buttons.
	private static readonly (MouseButton Button, int ImGui)[] Buttons =
	[
		(MouseButton.Left, 0), (MouseButton.Right, 1), (MouseButton.Middle, 2), (MouseButton.Button1, 3), (MouseButton.Button2, 4),
	];

	private readonly ImGuiOptions _options;
	private readonly IInputState? _input;
	private readonly IWindow? _window;
	private readonly IGraphicsFrame? _frame;
	private readonly bool[] _keysDown = new bool[Keys.Length];
	private readonly bool[] _buttonsDown = new bool[Buttons.Length];
	private ImGuiContextPtr _context;
	private ImGuiRenderer? _renderer;
	private nint _iniFile;
	private Vector2 _lastMouse = new(float.NaN);
	private ModifierKeys _lastModifiers;
	private bool _disposed;

	/// <summary>Creates the context (current from here on) with <paramref name="options"/>.</summary>
	public ImGuiHost(IOptions<ImGuiOptions> options, IInputState? input = null, IWindow? window = null, IGraphicsFrame? frame = null)
	{
		ArgumentNullException.ThrowIfNull(options);
		_options = options.Value;
		_input = input;
		_window = window;
		_frame = frame;

		_context = DearImGui.CreateContext();
		DearImGui.SetCurrentContext(_context);
		var io = DearImGui.GetIO();
		io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures | ImGuiBackendFlags.RendererHasVtxOffset;
		if (_options.KeyboardNavigation) io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
		if (_options.IniFile is { } ini) _iniFile = Marshal.StringToCoTaskMemUTF8(ini);
		io.IniFilename = (byte*)_iniFile;
		io.DisplaySize = DisplaySize();

		switch (_options.Style)
		{
			case ImGuiStyle.Light: DearImGui.StyleColorsLight(); break;
			case ImGuiStyle.Classic: DearImGui.StyleColorsClassic(); break;
			default: DearImGui.StyleColorsDark(); break;
		}

		if (_options.Scale is > 0 and not 1f)
		{
			var style = DearImGui.GetStyle();
			style.ScaleAllSizes(_options.Scale);
			style.FontScaleMain = _options.Scale;
		}
	}

	/// <summary>The Dear ImGui context.</summary>
	public ImGuiContextPtr Context => _context;

	/// <summary>Whether a frame is open: ImGui calls are valid (from First to the end of Render).</summary>
	public bool IsFrameOpen { get; private set; }

	/// <summary>ImGui uses the mouse (it hovers a window or drags something): the game should ignore it this frame.</summary>
	public bool WantCaptureMouse { get; private set; }

	/// <summary>ImGui uses the keyboard (a widget has focus): the game should ignore it this frame.</summary>
	public bool WantCaptureKeyboard { get; private set; }

	/// <summary>A text field of ImGui is active (show an on-screen keyboard on a device without one).</summary>
	public bool WantTextInput { get; private set; }

	/// <summary>The number of frames drawn (ImGui's frame count).</summary>
	public int FrameCount => IsCurrent() ? DearImGui.GetFrameCount() : 0;

	/// <summary>The draw calls the last frame issued (0 when nothing was drawn).</summary>
	public int LastDrawCalls => _renderer?.LastDrawCalls ?? 0;

	/// <summary>Whether a GPU renderer draws ImGui (false headless without rendering: the frames run, nothing is drawn).</summary>
	public bool IsRendering => _renderer is not null;

	internal ImGuiTextures Textures { get; } = new();

	/// <summary>
	/// Makes <paramref name="texture"/> drawable by ImGui (<c>ImGui.Image(id, size)</c>) and returns its id. The texture
	/// stays the caller's: unregister it before disposing it.
	/// </summary>
	public ImTextureID Register(ITexture texture)
	{
		ArgumentNullException.ThrowIfNull(texture);
		return new ImTextureID(Textures.Add(texture, owned: false, "ImGui user texture"));
	}

	/// <summary>Forgets a texture registered with <see cref="Register"/>. Returns false when it was not registered.</summary>
	public bool Unregister(ImTextureID id) => Textures.Release((nint)id.Handle);

	/// <summary>Creates the GPU renderer once the device exists (Init, after the graphics backend).</summary>
	internal void Initialize()
	{
		if (_renderer is not null || _frame is null) return;
		IGraphicsDevice device;
		try
		{
			device = _frame.Device;
		}
		catch (InvalidOperationException)
		{
			return; // a frame without a device (a CPU-only test frame)
		}

		_renderer = new ImGuiRenderer(device, Textures);
		Textures.BindGroupFactory = _renderer.CreateBindGroup;
	}

	/// <summary>Feeds the frame's input and opens an ImGui frame.</summary>
	internal void BeginFrame(float deltaSeconds)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		MakeCurrent();
		if (IsFrameOpen) DearImGui.EndFrame(); // a frame left open (no Render stage ran): close it so NewFrame stays balanced
		var io = DearImGui.GetIO();
		io.DisplaySize = DisplaySize();
		io.DeltaTime = deltaSeconds > 0 ? deltaSeconds : 1f / 60f;
		if (_options.Enabled && _input is not null) FeedInput(io, _input);
		DearImGui.NewFrame();
		IsFrameOpen = true;
	}

	/// <summary>Closes the frame and draws it over the frame's color target.</summary>
	internal void EndFrame()
	{
		if (!IsFrameOpen || _disposed) return;
		MakeCurrent();
		DearImGui.Render();
		IsFrameOpen = false;

		var io = DearImGui.GetIO();
		WantCaptureMouse = _options.Enabled && io.WantCaptureMouse;
		WantCaptureKeyboard = _options.Enabled && io.WantCaptureKeyboard;
		WantTextInput = _options.Enabled && io.WantTextInput;

		var drawData = DearImGui.GetDrawData();
		if (_renderer is { } renderer)
		{
			renderer.UpdateTextures(drawData);
			if (_options.Enabled && _frame is not null) renderer.Draw(drawData, _frame);
		}
		else
		{
			AcceptTexturesWithoutGpu(drawData);
		}
	}

	// Headless without a GPU: ImGui still needs its texture requests answered for the frames to run.
	private static void AcceptTexturesWithoutGpu(ImDrawDataPtr drawData)
	{
		var list = drawData.Handle->Textures;
		if (list is null) return;
		for (var i = 0; i < list->Size; i++)
		{
			var texture = list->Data[i];
			switch (texture.Status)
			{
				case ImTextureStatus.WantCreate:
					texture.SetTexID(new ImTextureID((nint)1));
					texture.SetStatus(ImTextureStatus.Ok);
					break;
				case ImTextureStatus.WantUpdates:
					texture.SetStatus(ImTextureStatus.Ok);
					break;
				case ImTextureStatus.WantDestroy when texture.UnusedFrames > 0:
					texture.SetTexID(ImTextureID.Null);
					texture.SetStatus(ImTextureStatus.Destroyed);
					break;
			}
		}
	}

	private void FeedInput(ImGuiIOPtr io, IInputState input)
	{
		var mouse = input.MousePosition;
		if (mouse != _lastMouse)
		{
			io.AddMousePosEvent(mouse.X, mouse.Y);
			_lastMouse = mouse;
		}

		for (var i = 0; i < Buttons.Length; i++)
		{
			var (button, index) = Buttons[i];
			var events = Edges(input.Pressed(button), input.Released(button), input.Down(button), ref _buttonsDown[i], out var first);
			if (events >= 1) io.AddMouseButtonEvent(index, first);
			if (events == 2) io.AddMouseButtonEvent(index, !first);
		}

		if (input.WheelDelta != 0) io.AddMouseWheelEvent(0, input.WheelDelta);

		var modifiers = input.Modifiers;
		if (modifiers != _lastModifiers)
		{
			io.AddKeyEvent(ImGuiKey.ModCtrl, modifiers.HasFlag(ModifierKeys.Control));
			io.AddKeyEvent(ImGuiKey.ModShift, modifiers.HasFlag(ModifierKeys.Shift));
			io.AddKeyEvent(ImGuiKey.ModAlt, modifiers.HasFlag(ModifierKeys.Alt));
			io.AddKeyEvent(ImGuiKey.ModSuper, modifiers.HasFlag(ModifierKeys.Gui));
			_lastModifiers = modifiers;
		}

		for (var i = 0; i < Keys.Length; i++)
		{
			var (key, imguiKey) = Keys[i];
			var events = Edges(input.Pressed(key), input.Released(key), input.Down(key), ref _keysDown[i], out var first);
			if (events >= 1) io.AddKeyEvent(imguiKey, first);
			if (events == 2) io.AddKeyEvent(imguiKey, !first);
		}

		var text = input.Text;
		while (!text.IsEmpty)
		{
			if (Rune.DecodeFromUtf16(text, out var rune, out var used) == System.Buffers.OperationStatus.Done) io.AddInputCharacter((uint)rune.Value);
			text = text[Math.Max(1, used)..];
		}
	}

	/// <summary>
	/// Turns a key's (or button's) state this frame into 0, 1 or 2 ImGui events, the first one <paramref name="first"/>
	/// (down or up) and a second one the opposite: a press and a release within one frame become both, in order (ImGui
	/// spreads them over frames), so quick taps are not lost.
	/// </summary>
	private static int Edges(bool pressed, bool released, bool down, ref bool sent, out bool first)
	{
		int events;
		if (pressed && released)
		{
			// Down now: released, then pressed again; up now: pressed, then released.
			first = !down;
			events = 2;
		}
		else if (down != sent)
		{
			first = down;
			events = 1;
		}
		else
		{
			first = false;
			events = 0;
		}

		sent = down;
		return events;
	}

	private Vector2 DisplaySize()
	{
		if (_window is { } window && window.Width > 0 && window.Height > 0) return new Vector2(window.Width, window.Height);
		if (_frame is { } frame && frame.Width > 0 && frame.Height > 0) return new Vector2(frame.Width, frame.Height);
		return new Vector2(960, 540);
	}

	private bool IsCurrent() => !_disposed && DearImGui.GetCurrentContext() == _context;

	private void MakeCurrent()
	{
		if (DearImGui.GetCurrentContext() != _context) DearImGui.SetCurrentContext(_context);
	}

	/// <summary>Releases the GPU resources (before the device goes, in Destroy).</summary>
	internal void ReleaseGpu()
	{
		Textures.BindGroupFactory = null;
		Textures.Clear();
		_renderer?.Dispose();
		_renderer = null;
	}

	/// <summary>Releases the GPU resources and destroys the context.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		ReleaseGpu();
		if (DearImGui.GetCurrentContext() == _context) DearImGui.SetCurrentContext(null);
		DearImGui.DestroyContext(_context);
		_context = default;
		if (_iniFile != 0) Marshal.FreeCoTaskMem(_iniFile);
		_iniFile = 0;
	}
}
