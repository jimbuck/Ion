namespace Ion.Extensions.Audio;

/// <summary>
/// A long sound that is decoded while it plays instead of when it loads: each voice playing it opens its own decoder
/// over the file and keeps a small ring of decoded audio ahead of the mixer (<see cref="AudioConfig.StreamBufferSeconds"/>),
/// so memory stays small whatever the track's length. The asset loaders stream files longer than
/// <see cref="AudioConfig.StreamLongerThan"/>; play it like any <see cref="ISoundEffect"/>.
/// </summary>
public sealed class StreamedSound : ISoundEffect
{
	private static long _nextId;
	private readonly Func<Stream> _open;

	/// <summary>
	/// Creates a streamed sound over the file that <paramref name="open"/> opens (a new seekable stream per call; each
	/// voice opens one), reading its format, length, channels and rate from it once.
	/// </summary>
	/// <exception cref="InvalidDataException">The file is not in a supported format, or is corrupt.</exception>
	public StreamedSound(string name, Func<Stream> open)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(open);
		Name = name;
		_open = open;

		var stream = open();
		Span<byte> header = stackalloc byte[16];
		var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
		Format = SoundDecoder.Detect(header[..read], name);
		stream.Position = 0;
		using var decoder = StreamDecoders.Open(stream, Format, name);
		Channels = decoder.Channels;
		SourceChannels = decoder.SourceChannels;
		SampleRate = decoder.SampleRate;
		Frames = decoder.Frames;
		Duration = Frames > 0 ? (float)((double)Frames / SampleRate) : 0f;
	}

	/// <inheritdoc/>
	public nint Id { get; } = (nint)Interlocked.Increment(ref _nextId);

	/// <inheritdoc/>
	public string Name { get; }

	/// <summary>The container format.</summary>
	public SoundFormat Format { get; }

	/// <inheritdoc/>
	public float Duration { get; }

	/// <summary>1 or 2: the channels the voices play.</summary>
	public int Channels { get; }

	/// <summary>The channels of the file (anything beyond the first two is dropped).</summary>
	public int SourceChannels { get; }

	/// <inheritdoc/>
	public int SampleRate { get; }

	/// <summary>The length in frames at <see cref="SampleRate"/>, or -1 when the file does not say.</summary>
	public long Frames { get; }

	/// <summary>A decoder of the file, positioned at its start.</summary>
	internal IStreamDecoder OpenDecoder() => StreamDecoders.Open(_open(), Format, Name);

	/// <summary>Nothing to release: each voice closes its own decoder.</summary>
	public void Dispose()
	{
	}
}

/// <summary>
/// The decoded audio ahead of one streamed voice: a ring of frames filled by the streamer (or, headless, before each
/// mix) and read by the audio thread. Frame indices are absolute from the cursor's start (a loop goes on counting), so
/// the two sides share only <see cref="Written"/> (frames published by the filler) and <see cref="ReadPosition"/>
/// (the frame the mixer has reached), and neither waits for the other.
/// </summary>
internal sealed class StreamCursor
{
	// Frames kept behind the mixer's position for interpolation (cubic reads one frame back).
	private const int History = 4;
	private const int Chunk = 1024;

	private IStreamDecoder? _decoder;
	private readonly float[] _scratch;
	private long _written;
	private long _read;
	private long _end = long.MaxValue;
	private long _underruns;
	private volatile bool _closed;

	public StreamCursor(StreamedSound sound, double startSeconds, bool loop, double bufferSeconds)
	{
		Sound = sound;
		Loop = loop;
		_decoder = sound.OpenDecoder();
		Channels = _decoder.Channels;
		SampleRate = _decoder.SampleRate;
		Capacity = Math.Max(Chunk * 2, (int)Math.Ceiling(bufferSeconds * SampleRate)) + History;
		Ring = new float[Capacity * Channels];
		_scratch = new float[Chunk * Channels];

		var start = (long)Math.Round(Math.Max(0, startSeconds) * SampleRate);
		if (start > 0)
		{
			if (sound.Frames > 0) start = loop ? start % sound.Frames : Math.Min(start, sound.Frames);
			_decoder.Seek(start);
		}
	}

	public StreamedSound Sound { get; }

	public bool Loop { get; }

	public int Channels { get; }

	public int SampleRate { get; }

	/// <summary>The ring's length in frames.</summary>
	public int Capacity { get; }

	/// <summary>Interleaved frames; frame <c>i</c> is at <c>(i % Capacity) * Channels</c> while it is between <see cref="ReadPosition"/> minus a few and <see cref="Written"/>.</summary>
	public float[] Ring { get; }

	/// <summary>Frames decoded and published (read on the audio thread).</summary>
	public long Written => Volatile.Read(ref _written);

	/// <summary>The frame the mixer has reached (written on the audio thread).</summary>
	public long ReadPosition
	{
		get => Volatile.Read(ref _read);
		set => Volatile.Write(ref _read, value);
	}

	/// <summary>The frame after the last one, once the decoder reached the end of a voice that does not loop.</summary>
	public long End => Volatile.Read(ref _end);

	/// <summary>Times the mixer caught up with the decoder and played silence.</summary>
	public long Underruns => Interlocked.Read(ref _underruns);

	public bool IsClosed => _closed;

	public void CountUnderrun() => Interlocked.Increment(ref _underruns);

	/// <summary>Stops filling; the filler releases the decoder (the ring stays valid for a mixer still reading it).</summary>
	public void Close() => _closed = true;

	/// <summary>
	/// Decodes as much as fits ahead of the mixer. False once the cursor is closed (its decoder released) and can be
	/// dropped. Called by one thread at a time: the streamer's, or the game thread before the cursor is handed over.
	/// </summary>
	public bool Fill()
	{
		if (_closed)
		{
			_decoder?.Dispose();
			_decoder = null;
			return false;
		}

		var decoder = _decoder;
		if (decoder is null || _end != long.MaxValue) return true;

		var written = _written;
		while (true)
		{
			var space = Capacity - History - (int)(written - ReadPosition);
			if (space <= 0) break;
			var want = Math.Min(space, Chunk);
			var frames = decoder.Read(_scratch.AsSpan(0, want * Channels));
			if (frames == 0)
			{
				// The end of the file: a loop goes on from the start; otherwise the voice ends at the last frame.
				if (Loop && written > 0)
				{
					decoder.Seek(0);
					frames = decoder.Read(_scratch.AsSpan(0, want * Channels));
				}

				if (frames == 0)
				{
					Volatile.Write(ref _end, written);
					break;
				}
			}

			// Copy into the ring (wrapping), then publish the frames.
			var at = (int)(written % Capacity);
			var first = Math.Min(frames, Capacity - at);
			_scratch.AsSpan(0, first * Channels).CopyTo(Ring.AsSpan(at * Channels));
			if (first < frames) _scratch.AsSpan(first * Channels, (frames - first) * Channels).CopyTo(Ring);
			written += frames;
			Volatile.Write(ref _written, written);
		}

		return true;
	}
}

/// <summary>
/// Keeps the rings of the streamed voices full: on a background thread of its own while a device plays, or on demand
/// (<see cref="Pump"/>, called before each mix by the null output) so headless runs stay deterministic.
/// </summary>
public sealed class AudioStreamer : IDisposable
{
	private readonly Lock _lock = new();
	private readonly List<StreamCursor> _cursors = [];
	private StreamCursor[] _snapshot = new StreamCursor[8];
	private Thread? _thread;
	private volatile bool _running;
	private readonly AutoResetEvent _wake = new(false);

	/// <summary>The streamed voices being filled.</summary>
	public int Count
	{
		get
		{
			lock (_lock) return _cursors.Count;
		}
	}

	/// <summary>Whether the background thread runs.</summary>
	public bool IsThreaded => _running;

	internal void Add(StreamCursor cursor)
	{
		lock (_lock) _cursors.Add(cursor);
		_wake.Set();
	}

	/// <summary>Fills every ring as far as it goes and drops the closed ones.</summary>
	public void Pump()
	{
		int count;
		lock (_lock)
		{
			count = _cursors.Count;
			if (_snapshot.Length < count) _snapshot = new StreamCursor[Math.Max(count, _snapshot.Length * 2)];
			_cursors.CopyTo(_snapshot);
		}

		var anyClosed = false;
		for (var i = 0; i < count; i++)
		{
			if (!_snapshot[i].Fill()) anyClosed = true;
			_snapshot[i] = null!;
		}

		if (!anyClosed) return;
		lock (_lock) _cursors.RemoveAll(static c => c.IsClosed);
	}

	/// <summary>Starts the background thread (a few milliseconds between fills).</summary>
	public void Start()
	{
		if (_running) return;
		_running = true;
		_thread = new Thread(() =>
		{
			while (_running)
			{
				Pump();
				_wake.WaitOne(5);
			}
		})
		{
			IsBackground = true,
			Name = "Ion audio streamer",
			Priority = ThreadPriority.AboveNormal,
		};
		_thread.Start();
	}

	/// <summary>Stops the background thread.</summary>
	public void Stop()
	{
		if (!_running) return;
		_running = false;
		_wake.Set();
		_thread?.Join();
		_thread = null;
	}

	/// <summary>Stops the thread and releases every decoder.</summary>
	public void Dispose()
	{
		Stop();
		lock (_lock)
		{
			foreach (var cursor in _cursors)
			{
				cursor.Close();
				cursor.Fill();
			}

			_cursors.Clear();
		}

		_wake.Dispose();
	}
}
