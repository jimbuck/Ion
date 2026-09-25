using System.Text;

using Microsoft.Extensions.Logging;

using static Ion.Extensions.Audio.OpenAlNative;

namespace Ion.Extensions.Audio;

/// <summary>
/// Plays the mix through OpenAL: one streaming source with a ring of <see cref="BufferCount"/> queued buffers, refilled
/// from the mixer on a dedicated thread as the device consumes them. Loads OpenAL Soft (shipped by
/// <c>Silk.NET.OpenAL.Soft.Native</c> for Windows, macOS and Linux on x64 and arm64), falling back to the system OpenAL
/// (<c>libopenal.so.1</c>, <c>OpenAL32.dll</c>, or OpenAL.framework on Apple platforms).
/// </summary>
/// <remarks>
/// Uses float32 buffers when the implementation has <c>AL_EXT_FLOAT32</c> (OpenAL Soft does), else converts to 16-bit
/// on the audio thread. The refill loop polls about four times per buffer and never allocates. If the device is
/// disconnected or an OpenAL call fails on the audio thread, <see cref="IsRunning"/> becomes false and the audio manager
/// switches to the null output.
/// </remarks>
public sealed unsafe class OpenAlAudioOutput : IAudioOutput
{
	private readonly ILogger? _logger;
	private readonly string? _deviceName;

	private OpenAlNative? _al;
	private nint _device;
	private nint _context;
	private uint _source;
	private uint[] _buffers = [];
	private int _bufferFormat;
	private bool _float32;
	private float[] _mix = [];
	private short[] _pcm16 = [];
	private AudioRenderCallback? _callback;
	private AudioFormat _format;
	private Thread? _thread;
	private volatile bool _stopRequested;
	private volatile bool _running;
	private int _sleepMs;
	private long _underruns;

	/// <summary>
	/// Creates an OpenAL output. Nothing is opened until <see cref="Start"/>.
	/// </summary>
	/// <param name="bufferFrames">Frames per queued buffer.</param>
	/// <param name="bufferCount">Buffers in the ring (at least 2).</param>
	/// <param name="deviceName">The device to open, or null for the default device.</param>
	/// <param name="logger">Optional logger for device information.</param>
	public OpenAlAudioOutput(int bufferFrames = 512, int bufferCount = 4, string? deviceName = null, ILogger? logger = null)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(bufferFrames, 64);
		ArgumentOutOfRangeException.ThrowIfLessThan(bufferCount, 2);
		BufferSize = bufferFrames;
		BufferCount = bufferCount;
		_deviceName = string.IsNullOrEmpty(deviceName) ? null : deviceName;
		_logger = logger;
	}

	/// <inheritdoc/>
	public string Name => "OpenAL";

	/// <inheritdoc/>
	public int BufferSize { get; }

	/// <summary>Buffers in the streaming ring.</summary>
	public int BufferCount { get; }

	/// <summary>The native library that was loaded, once started.</summary>
	public string? LibraryName { get; private set; }

	/// <summary>The name of the opened device, once started.</summary>
	public string? DeviceName { get; private set; }

	/// <summary>Whether the output uses float32 buffers (<c>AL_EXT_FLOAT32</c>) rather than 16-bit.</summary>
	public bool UsesFloat32 => _float32;

	/// <summary>Times the source ran dry and was restarted.</summary>
	public long Underruns => Interlocked.Read(ref _underruns);

	/// <summary>The error that stopped the audio thread, if any.</summary>
	public Exception? Error { get; private set; }

	/// <inheritdoc/>
	public bool IsRunning => _running;

	/// <summary>
	/// Lists the playback devices OpenAL reports, or an empty list when no OpenAL library can be loaded.
	/// </summary>
	public static IReadOnlyList<string> GetDeviceNames()
	{
		try
		{
			using var al = OpenAlNative.Load();
			var all = al.IsAlcExtensionPresent(0, "ALC_ENUMERATE_ALL_EXT");
			return al.AlcStringList(0, all ? ALC_ALL_DEVICES_SPECIFIER : ALC_DEVICE_SPECIFIER);
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
		{
			return [];
		}
	}

	/// <inheritdoc/>
	public void Start(AudioFormat format, AudioRenderCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		if (_running) throw new InvalidOperationException("The OpenAL output is already running.");
		if (format.Channels != 2) throw new NotSupportedException($"The OpenAL output plays stereo only, not {format.Channels} channels.");

		_callback = callback;
		_format = format;
		_mix = new float[BufferSize * format.Channels];

		try
		{
			_open(format);
			_pcm16 = _float32 ? [] : new short[_mix.Length];

			// Prime every buffer, then play.
			var al = _al!;
			for (var i = 0; i < _buffers.Length; i++) _fill(_buffers[i]);
			fixed (uint* buffers = _buffers) al.alSourceQueueBuffers(_source, _buffers.Length, buffers);
			al.alSourcePlay(_source);
			_check("start the source");
		}
		catch
		{
			_close();
			_callback = null;
			throw;
		}

		_sleepMs = Math.Max(1, (int)(1000L * BufferSize / format.SampleRate / 4));
		_stopRequested = false;
		_running = true;
		_thread = new Thread(_run)
		{
			IsBackground = true,
			Name = "Ion audio (OpenAL)",
			Priority = ThreadPriority.AboveNormal,
		};
		_thread.Start();

		_logger?.LogInformation("Audio output: OpenAL device '{Device}' ({Library}), {Rate} Hz stereo {Format}, {Count} x {Frames} frames.",
			DeviceName, LibraryName, format.SampleRate, _float32 ? "float32" : "16-bit", BufferCount, BufferSize);
	}

	/// <inheritdoc/>
	public void Stop()
	{
		_stopRequested = true;
		var thread = _thread;
		if (thread is not null && thread != Thread.CurrentThread) thread.Join(2000);
		_thread = null;
		_running = false;
		_close();
		_callback = null;
	}

	/// <inheritdoc/>
	public void Dispose() => Stop();

	private void _open(AudioFormat format)
	{
		var al = OpenAlNative.Load();
		_al = al;
		LibraryName = al.LibraryName;

		if (_deviceName is null)
		{
			_device = al.alcOpenDevice(null);
		}
		else
		{
			var name = Encoding.UTF8.GetBytes(_deviceName + "\0");
			fixed (byte* p = name) _device = al.alcOpenDevice(p);
		}

		if (_device == 0)
		{
			throw new InvalidOperationException(_deviceName is null
				? $"OpenAL ({LibraryName}) found no audio output device."
				: $"OpenAL ({LibraryName}) could not open the audio device '{_deviceName}'.");
		}

		var attributes = stackalloc int[] { ALC_FREQUENCY, format.SampleRate, 0 };
		_context = al.alcCreateContext(_device, attributes);
		if (_context == 0) throw new InvalidOperationException($"OpenAL could not create a context (ALC error 0x{al.alcGetError(_device):X}).");
		if (al.alcMakeContextCurrent(_context) == 0) throw new InvalidOperationException("OpenAL could not make its context current.");

		DeviceName = al.AlcString(_device, ALC_DEVICE_SPECIFIER);

		_float32 = al.IsAlExtensionPresent("AL_EXT_FLOAT32");
		_bufferFormat = _float32 ? AL_FORMAT_STEREO_FLOAT32 : AL_FORMAT_STEREO16;

		al.alGetError(); // clear
		uint source;
		al.alGenSources(1, &source);
		_source = source;
		_buffers = new uint[BufferCount];
		fixed (uint* buffers = _buffers) al.alGenBuffers(_buffers.Length, buffers);
		_check("create the source and buffers");

		// The mix plays unchanged: no 3D positioning, no attenuation.
		al.alSourcei(_source, AL_SOURCE_RELATIVE, 1);
		al.alSourcef(_source, AL_GAIN, 1f);
		al.alSourcef(_source, AL_PITCH, 1f);
		_check("configure the source");
	}

	private void _run()
	{
		var al = _al!;
		try
		{
			uint buffer;
			int processed, state, connected;
			var hasDisconnect = al.IsAlcExtensionPresent(_device, "ALC_EXT_disconnect");

			while (!_stopRequested)
			{
				processed = 0;
				al.alGetSourcei(_source, AL_BUFFERS_PROCESSED, &processed);
				while (processed-- > 0 && !_stopRequested)
				{
					al.alSourceUnqueueBuffers(_source, 1, &buffer);
					_fill(buffer);
					al.alSourceQueueBuffers(_source, 1, &buffer);
				}

				state = 0;
				al.alGetSourcei(_source, AL_SOURCE_STATE, &state);
				if (state != AL_PLAYING && !_stopRequested)
				{
					Interlocked.Increment(ref _underruns);
					al.alSourcePlay(_source);
				}

				var error = al.alGetError();
				if (error != AL_NO_ERROR) throw new InvalidOperationException($"OpenAL error 0x{error:X} while streaming.");

				if (hasDisconnect)
				{
					connected = 1;
					al.alcGetIntegerv(_device, ALC_CONNECTED, 1, &connected);
					if (connected == 0) throw new InvalidOperationException("The OpenAL device was disconnected.");
				}

				Thread.Sleep(_sleepMs);
			}
		}
		catch (Exception ex)
		{
			Error = ex;
		}
		finally
		{
			_running = false;
		}
	}

	private void _fill(uint buffer)
	{
		var al = _al!;
		var mix = _mix;
		_callback!(mix);

		if (_float32)
		{
			fixed (float* data = mix) al.alBufferData(buffer, _bufferFormat, data, mix.Length * sizeof(float), _format.SampleRate);
			return;
		}

		var pcm = _pcm16;
		for (var i = 0; i < mix.Length; i++)
		{
			var s = mix[i] * 32767f;
			pcm[i] = (short)(s >= 32767f ? 32767 : s <= -32768f ? -32768 : (int)s);
		}

		fixed (short* data = pcm) al.alBufferData(buffer, _bufferFormat, data, pcm.Length * sizeof(short), _format.SampleRate);
	}

	private void _check(string what)
	{
		var error = _al!.alGetError();
		if (error != AL_NO_ERROR) throw new InvalidOperationException($"OpenAL could not {what} (AL error 0x{error:X}).");
	}

	private void _close()
	{
		var al = _al;
		if (al is null) return;

		if (_source != 0)
		{
			var source = _source;
			al.alSourceStop(source);
			al.alSourcei(source, AL_BUFFER, 0);
			al.alDeleteSources(1, &source);
			_source = 0;
		}

		if (_buffers.Length > 0)
		{
			fixed (uint* buffers = _buffers) al.alDeleteBuffers(_buffers.Length, buffers);
			_buffers = [];
		}

		if (_context != 0)
		{
			al.alcMakeContextCurrent(0);
			al.alcDestroyContext(_context);
			_context = 0;
		}

		if (_device != 0)
		{
			al.alcCloseDevice(_device);
			_device = 0;
		}

		al.Dispose();
		_al = null;
	}
}
