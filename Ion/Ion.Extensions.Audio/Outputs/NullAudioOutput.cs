namespace Ion.Extensions.Audio;

/// <summary>
/// An output with no device. It pulls the mixer on the calling thread when told how much time has passed
/// (<see cref="Advance"/>, called by the audio system every frame with the game clock) or how many frames to render
/// (<see cref="Render"/>), so headless runs mix deterministically: the same commands and frame times give bit-identical
/// buffers. Turn on <see cref="CaptureEnabled"/> to keep everything rendered for inspection.
/// </summary>
public sealed class NullAudioOutput : IAudioOutput
{
	private readonly Lock _lock = new();
	private float[] _buffer;
	private float[] _capture = [];
	private int _captured; // samples
	private int _lastSamples;
	private AudioRenderCallback? _callback;
	private AudioFormat _format;
	private long _framesRendered;
	private long _clockFrames;

	/// <summary>
	/// Creates a null output that renders at most <paramref name="bufferFrames"/> frames per callback.
	/// </summary>
	public NullAudioOutput(int bufferFrames = 512)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(bufferFrames, 1);
		BufferSize = bufferFrames;
		_buffer = [];
	}

	/// <inheritdoc/>
	public string Name => "Null";

	/// <inheritdoc/>
	public int BufferSize { get; }

	/// <inheritdoc/>
	public bool IsRunning => _callback is not null;

	/// <summary>The format given to <see cref="Start"/>.</summary>
	public AudioFormat Format => _format;

	/// <summary>Frames rendered since <see cref="Start"/>.</summary>
	public long FramesRendered
	{
		get
		{
			lock (_lock) return _framesRendered;
		}
	}

	/// <summary>
	/// When true, every rendered sample is appended to <see cref="Captured"/>. Off by default so long headless runs do not
	/// grow memory.
	/// </summary>
	public bool CaptureEnabled { get; set; }

	/// <summary>
	/// A copy of the samples captured while <see cref="CaptureEnabled"/> was on (interleaved, <see cref="Format"/>'s channels).
	/// </summary>
	public float[] Captured
	{
		get
		{
			lock (_lock) return _capture.AsSpan(0, _captured).ToArray();
		}
	}

	/// <summary>A copy of the samples of the last callback.</summary>
	public float[] LastBuffer
	{
		get
		{
			lock (_lock) return _buffer.AsSpan(0, _lastSamples).ToArray();
		}
	}

	/// <summary>Forgets the captured samples.</summary>
	public void ClearCapture()
	{
		lock (_lock) _captured = 0;
	}

	/// <inheritdoc/>
	public void Start(AudioFormat format, AudioRenderCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ArgumentOutOfRangeException.ThrowIfLessThan(format.SampleRate, 1, nameof(format));
		ArgumentOutOfRangeException.ThrowIfLessThan(format.Channels, 1, nameof(format));

		lock (_lock)
		{
			_format = format;
			_callback = callback;
			_buffer = new float[BufferSize * format.Channels];
			_framesRendered = 0;
			_clockFrames = 0;
		}
	}

	/// <inheritdoc/>
	public void Stop()
	{
		lock (_lock) _callback = null;
	}

	/// <summary>
	/// Renders the audio due by <paramref name="elapsed"/> (total game time since start): the frames between the last
	/// call and <c>elapsed * SampleRate</c>, in buffers of at most <see cref="BufferSize"/> frames. At most one second is
	/// rendered per call; a longer gap (a hitch or a debugger pause) is skipped. Returns the frames rendered.
	/// </summary>
	public int Advance(TimeSpan elapsed)
	{
		lock (_lock)
		{
			if (_callback is null || elapsed <= TimeSpan.Zero) return 0;

			var due = (long)((Int128)elapsed.Ticks * _format.SampleRate / TimeSpan.TicksPerSecond);
			var frames = due - _clockFrames;
			if (frames <= 0) return 0;

			_clockFrames = due;
			return _render((int)Math.Min(frames, _format.SampleRate));
		}
	}

	/// <summary>
	/// Renders <paramref name="frames"/> frames now, in buffers of at most <see cref="BufferSize"/> frames, without
	/// touching the clock position used by <see cref="Advance"/>. Returns the frames rendered (0 when not started).
	/// </summary>
	public int Render(int frames)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(frames);
		lock (_lock)
		{
			if (_callback is null) return 0;
			return _render(frames);
		}
	}

	private int _render(int frames)
	{
		var channels = _format.Channels;
		var left = frames;
		while (left > 0)
		{
			var chunk = Math.Min(left, BufferSize);
			var span = _buffer.AsSpan(0, chunk * channels);
			_callback!(span);
			_lastSamples = span.Length;

			if (CaptureEnabled)
			{
				if (_captured + span.Length > _capture.Length)
				{
					Array.Resize(ref _capture, Math.Max(_capture.Length * 2, _captured + span.Length));
				}
				span.CopyTo(_capture.AsSpan(_captured));
				_captured += span.Length;
			}

			left -= chunk;
			_framesRendered += chunk;
		}

		return frames;
	}

	/// <inheritdoc/>
	public void Dispose() => Stop();
}
