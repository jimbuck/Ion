using System.Numerics;
using System.Text;

namespace Ion;

/// <summary>
/// The binary input recording format written by <see cref="InputRecorder"/> and read by <see cref="InputPlayer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Little-endian. A header of the four ASCII bytes <c>IONI</c> and a format version byte (<see cref="Version"/>), then a
/// sequence of frame blocks: the frame number and the event count (both 7-bit encoded unsigned integers), then the
/// events. Frames without events are not written. The last block, written when the recorder is disposed, has an event
/// count of zero and gives the last recorded frame, so playback covers trailing frames without input too.
/// </para>
/// <para>
/// Each event is its <see cref="InputEventKind"/> byte and a payload: <c>Key</c> the key (7-bit int), a flags byte (1 down,
/// 2 repeat) and the modifiers byte; <c>MouseButton</c> the button and down bytes; <c>MouseMove</c> two floats (x, y);
/// <c>Wheel</c> a float; <c>Text</c> the UTF-16 code unit (ushort); <c>GamepadConnection</c> the slot and connected bytes;
/// <c>GamepadButton</c> the slot, button and down bytes; <c>GamepadAxis</c> the slot and axis bytes and a float;
/// <c>ReleaseAll</c> nothing.
/// </para>
/// </remarks>
public static class InputRecordingFormat
{
	/// <summary>The file signature.</summary>
	public static ReadOnlySpan<byte> Magic => "IONI"u8;

	/// <summary>The format version this build writes and reads.</summary>
	public const byte Version = 1;

	internal static void Write(BinaryWriter writer, in InputEvent e)
	{
		writer.Write((byte)e.Kind);
		switch (e.Kind)
		{
			case InputEventKind.Key:
				writer.Write7BitEncodedInt(e.Code);
				writer.Write((byte)((e.Down ? 1 : 0) | (e.Repeat ? 2 : 0)));
				writer.Write((byte)e.Modifiers);
				break;
			case InputEventKind.MouseButton:
				writer.Write((byte)e.Code);
				writer.Write(e.Down);
				break;
			case InputEventKind.MouseMove:
				writer.Write(e.Value.X);
				writer.Write(e.Value.Y);
				break;
			case InputEventKind.Wheel:
				writer.Write(e.Value.X);
				break;
			case InputEventKind.Text:
				writer.Write((ushort)e.Code);
				break;
			case InputEventKind.GamepadConnection:
				writer.Write((byte)e.Index);
				writer.Write(e.Down);
				break;
			case InputEventKind.GamepadButton:
				writer.Write((byte)e.Index);
				writer.Write((byte)e.Code);
				writer.Write(e.Down);
				break;
			case InputEventKind.GamepadAxis:
				writer.Write((byte)e.Index);
				writer.Write((byte)e.Code);
				writer.Write(e.Value.X);
				break;
			case InputEventKind.ReleaseAll:
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(e), e.Kind, "Unknown input event kind.");
		}
	}

	internal static InputEvent Read(BinaryReader reader)
	{
		var kind = (InputEventKind)reader.ReadByte();
		switch (kind)
		{
			case InputEventKind.Key:
				var key = reader.Read7BitEncodedInt();
				var flags = reader.ReadByte();
				var modifiers = (ModifierKeys)reader.ReadByte();
				return new InputEvent(kind, key, Down: (flags & 1) != 0, Repeat: (flags & 2) != 0, Modifiers: modifiers);
			case InputEventKind.MouseButton:
				return new InputEvent(kind, reader.ReadByte(), Down: reader.ReadBoolean());
			case InputEventKind.MouseMove:
				return new InputEvent(kind, Value: new Vector2(reader.ReadSingle(), reader.ReadSingle()));
			case InputEventKind.Wheel:
				return new InputEvent(kind, Value: new Vector2(reader.ReadSingle(), 0));
			case InputEventKind.Text:
				return new InputEvent(kind, reader.ReadUInt16());
			case InputEventKind.GamepadConnection:
				return new InputEvent(kind, Index: reader.ReadByte(), Down: reader.ReadBoolean());
			case InputEventKind.GamepadButton:
				var buttonPad = reader.ReadByte();
				return new InputEvent(kind, reader.ReadByte(), buttonPad, reader.ReadBoolean());
			case InputEventKind.GamepadAxis:
				var axisPad = reader.ReadByte();
				var axis = reader.ReadByte();
				return new InputEvent(kind, axis, axisPad, Value: new Vector2(reader.ReadSingle(), 0));
			case InputEventKind.ReleaseAll:
				return new InputEvent(kind);
			default:
				throw new InvalidDataException($"Unknown input event kind {(byte)kind} in the input recording.");
		}
	}
}

/// <summary>
/// Writes every input event an <see cref="InputTracker"/> applies to a stream in <see cref="InputRecordingFormat"/>, tagged
/// with its frame number. Attach it with <see cref="InputServiceCollectionExtensions.AddInputRecording"/>, or set it as
/// <see cref="InputTracker.Recorder"/>. Dispose it to write the end of the recording and close the stream.
/// </summary>
public sealed class InputRecorder : IInputRecorder, IInputTrackerHook, IDisposable
{
	private readonly BinaryWriter _writer;
	private readonly List<InputEvent> _frameEvents = [];
	private uint _frame;
	private bool _started;
	private bool _disposed;

	/// <summary>
	/// Records into a new file at <paramref name="path"/> (replacing an existing one). The directory is created if needed.
	/// </summary>
	public InputRecorder(string path) : this(_create(path), leaveOpen: false)
	{
	}

	/// <summary>
	/// Records into <paramref name="stream"/>.
	/// </summary>
	/// <param name="stream">A writable stream.</param>
	/// <param name="leaveOpen">Whether to leave the stream open when the recorder is disposed.</param>
	public InputRecorder(Stream stream, bool leaveOpen = false)
	{
		ArgumentNullException.ThrowIfNull(stream);
		_writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen);
		_writer.Write(InputRecordingFormat.Magic);
		_writer.Write(InputRecordingFormat.Version);
	}

	/// <summary>The number of events recorded so far.</summary>
	public long EventCount { get; private set; }

	/// <inheritdoc/>
	public void Attach(InputTracker tracker) => tracker.Recorder = this;

	/// <inheritdoc/>
	public void BeginFrame(uint frame)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_flushFrame();
		_frame = frame;
		_started = true;
	}

	/// <inheritdoc/>
	public void OnKey(Key key, bool down, bool repeat, ModifierKeys modifiers) => _add(InputEvent.ForKey(key, down, repeat, modifiers));

	/// <inheritdoc/>
	public void OnMouseButton(MouseButton button, bool down) => _add(InputEvent.ForMouseButton(button, down));

	/// <inheritdoc/>
	public void OnMouseMove(Vector2 position) => _add(InputEvent.ForMouseMove(position));

	/// <inheritdoc/>
	public void OnWheel(float delta) => _add(InputEvent.ForWheel(delta));

	/// <inheritdoc/>
	public void OnText(char character) => _add(InputEvent.ForText(character));

	/// <inheritdoc/>
	public void OnGamepadConnected(int index, bool connected) => _add(InputEvent.ForGamepadConnection(index, connected));

	/// <inheritdoc/>
	public void OnGamepadButton(int index, GamepadButton button, bool down) => _add(InputEvent.ForGamepadButton(index, button, down));

	/// <inheritdoc/>
	public void OnGamepadAxis(int index, GamepadAxis axis, float value) => _add(InputEvent.ForGamepadAxis(index, axis, value));

	/// <inheritdoc/>
	public void ReleaseAll() => _add(InputEvent.ForReleaseAll());

	/// <summary>
	/// Writes the events of the current frame so far and flushes the stream.
	/// </summary>
	public void Flush()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_flushFrame();
		_writer.Flush();
	}

	/// <summary>
	/// Writes the pending events and the end marker (the last frame), then closes the stream unless it was left open.
	/// </summary>
	public void Dispose()
	{
		if (_disposed) return;

		try
		{
			_flushFrame();
			if (_started)
			{
				_writer.Write7BitEncodedInt64(_frame);
				_writer.Write7BitEncodedInt(0);
			}

			_writer.Flush();
		}
		finally
		{
			_disposed = true;
			_writer.Dispose();
		}
	}

	private void _add(in InputEvent e)
	{
		if (_disposed) return;
		_frameEvents.Add(e);
		EventCount++;
	}

	private void _flushFrame()
	{
		if (_frameEvents.Count == 0) return;

		_writer.Write7BitEncodedInt64(_frame);
		_writer.Write7BitEncodedInt(_frameEvents.Count);
		foreach (var e in _frameEvents) InputRecordingFormat.Write(_writer, e);
		_frameEvents.Clear();
	}

	private static FileStream _create(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		var directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
	}
}

/// <summary>
/// Replays an <see cref="InputRecordingFormat"/> recording: at each frame it applies the events recorded for that frame
/// number (and any earlier ones it has not applied yet). Attach it with
/// <see cref="InputServiceCollectionExtensions.AddInputPlayback"/>, or set it as <see cref="InputTracker.Playback"/>, where
/// it replaces device input until the recording ends; or call <see cref="Play"/> to replay into any
/// <see cref="IInputEventSink"/>.
/// </summary>
public sealed class InputPlayer : IInputPlayback, IInputTrackerHook
{
	private readonly (uint Frame, int Start, int Count)[] _frames;
	private readonly InputEvent[] _events;
	private int _next;
	private bool _ended;

	/// <summary>
	/// Loads the recording at <paramref name="path"/>.
	/// </summary>
	/// <exception cref="FileNotFoundException">The file does not exist.</exception>
	/// <exception cref="InvalidDataException">The file is not an input recording of a supported version.</exception>
	public InputPlayer(string path) : this(File.OpenRead(path), leaveOpen: false)
	{
	}

	/// <summary>
	/// Loads a recording from <paramref name="stream"/> (read to its end).
	/// </summary>
	/// <exception cref="InvalidDataException">The stream is not an input recording of a supported version.</exception>
	public InputPlayer(Stream stream, bool leaveOpen = false)
	{
		ArgumentNullException.ThrowIfNull(stream);
		using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen);

		Span<byte> magic = stackalloc byte[4];
		if (reader.Read(magic) != 4 || !magic.SequenceEqual(InputRecordingFormat.Magic)) throw new InvalidDataException("Not an Ion input recording (missing the IONI signature).");
		var version = reader.ReadByte();
		if (version != InputRecordingFormat.Version) throw new InvalidDataException($"Unsupported input recording version {version}; this build reads version {InputRecordingFormat.Version}.");

		var frames = new List<(uint, int, int)>();
		var events = new List<InputEvent>();
		uint? end = null;
		while (reader.BaseStream.Position < reader.BaseStream.Length)
		{
			var frame = (uint)reader.Read7BitEncodedInt64();
			var count = reader.Read7BitEncodedInt();
			if (count == 0)
			{
				end = frame;
				break;
			}

			frames.Add((frame, events.Count, count));
			for (var i = 0; i < count; i++) events.Add(InputRecordingFormat.Read(reader));
		}

		_frames = [.. frames];
		_events = [.. events];
		LastFrame = end ?? (_frames.Length > 0 ? _frames[^1].Frame : 0);
	}

	/// <summary>The last frame the recording covers.</summary>
	public uint LastFrame { get; }

	/// <summary>The total number of recorded events.</summary>
	public int EventCount => _events.Length;

	/// <summary>The recorded events, frame by frame.</summary>
	public IEnumerable<(uint Frame, InputEvent Event)> Events
	{
		get
		{
			foreach (var (frame, start, count) in _frames)
			{
				for (var i = start; i < start + count; i++) yield return (frame, _events[i]);
			}
		}
	}

	/// <inheritdoc/>
	public bool IsPlaying => !_ended;

	/// <inheritdoc/>
	public void Attach(InputTracker tracker) => tracker.Playback = this;

	/// <inheritdoc/>
	public void Play(uint frame, IInputEventSink sink)
	{
		ArgumentNullException.ThrowIfNull(sink);

		while (_next < _frames.Length && _frames[_next].Frame <= frame)
		{
			var (_, start, count) = _frames[_next++];
			for (var i = start; i < start + count; i++) _events[i].ApplyTo(sink);
		}

		if (frame > LastFrame && _next >= _frames.Length) _ended = true;
	}

	/// <summary>
	/// Starts the recording again from its first frame.
	/// </summary>
	public void Rewind()
	{
		_next = 0;
		_ended = false;
	}
}
