using System.Runtime.InteropServices;

namespace Ion.Extensions.Audio;

/// <summary>
/// The few OpenAL 1.1 entry points the streaming output needs, bound through function pointers from a library loaded
/// with <see cref="NativeLibrary"/>: no reflection, no marshalling stubs, nothing for the trimmer or NativeAOT to warn
/// about. Tries OpenAL Soft first (as shipped by <c>Silk.NET.OpenAL.Soft.Native</c>), then the system OpenAL.
/// </summary>
internal sealed unsafe class OpenAlNative : IDisposable
{
	public const int AL_NO_ERROR = 0;
	public const int AL_SOURCE_RELATIVE = 0x202;
	public const int AL_PITCH = 0x1003;
	public const int AL_GAIN = 0x100A;
	public const int AL_BUFFER = 0x1009;
	public const int AL_SOURCE_STATE = 0x1010;
	public const int AL_PLAYING = 0x1012;
	public const int AL_BUFFERS_PROCESSED = 0x1016;
	public const int AL_FORMAT_STEREO16 = 0x1103;
	public const int AL_FORMAT_STEREO_FLOAT32 = 0x10011;

	public const int ALC_FREQUENCY = 0x1007;
	public const int ALC_DEVICE_SPECIFIER = 0x1005;
	public const int ALC_ALL_DEVICES_SPECIFIER = 0x1013;
	public const int ALC_CONNECTED = 0x313;

	private nint _library;

	// ALC
	public readonly delegate* unmanaged[Cdecl]<byte*, nint> alcOpenDevice;
	public readonly delegate* unmanaged[Cdecl]<nint, byte> alcCloseDevice;
	public readonly delegate* unmanaged[Cdecl]<nint, int*, nint> alcCreateContext;
	public readonly delegate* unmanaged[Cdecl]<nint, byte> alcMakeContextCurrent;
	public readonly delegate* unmanaged[Cdecl]<nint, void> alcDestroyContext;
	public readonly delegate* unmanaged[Cdecl]<nint, int> alcGetError;
	public readonly delegate* unmanaged[Cdecl]<nint, int, byte*> alcGetString;
	public readonly delegate* unmanaged[Cdecl]<nint, byte*, byte> alcIsExtensionPresent;
	public readonly delegate* unmanaged[Cdecl]<nint, int, int, int*, void> alcGetIntegerv;

	// AL
	public readonly delegate* unmanaged[Cdecl]<int> alGetError;
	public readonly delegate* unmanaged[Cdecl]<byte*, byte> alIsExtensionPresent;
	public readonly delegate* unmanaged[Cdecl]<int, uint*, void> alGenSources;
	public readonly delegate* unmanaged[Cdecl]<int, uint*, void> alDeleteSources;
	public readonly delegate* unmanaged[Cdecl]<int, uint*, void> alGenBuffers;
	public readonly delegate* unmanaged[Cdecl]<int, uint*, void> alDeleteBuffers;
	public readonly delegate* unmanaged[Cdecl]<uint, int, void*, int, int, void> alBufferData;
	public readonly delegate* unmanaged[Cdecl]<uint, int, uint*, void> alSourceQueueBuffers;
	public readonly delegate* unmanaged[Cdecl]<uint, int, uint*, void> alSourceUnqueueBuffers;
	public readonly delegate* unmanaged[Cdecl]<uint, int, int*, void> alGetSourcei;
	public readonly delegate* unmanaged[Cdecl]<uint, int, int, void> alSourcei;
	public readonly delegate* unmanaged[Cdecl]<uint, int, float, void> alSourcef;
	public readonly delegate* unmanaged[Cdecl]<uint, void> alSourcePlay;
	public readonly delegate* unmanaged[Cdecl]<uint, void> alSourceStop;

	private OpenAlNative(nint library, string name)
	{
		_library = library;
		LibraryName = name;

		alcOpenDevice = (delegate* unmanaged[Cdecl]<byte*, nint>)_export("alcOpenDevice");
		alcCloseDevice = (delegate* unmanaged[Cdecl]<nint, byte>)_export("alcCloseDevice");
		alcCreateContext = (delegate* unmanaged[Cdecl]<nint, int*, nint>)_export("alcCreateContext");
		alcMakeContextCurrent = (delegate* unmanaged[Cdecl]<nint, byte>)_export("alcMakeContextCurrent");
		alcDestroyContext = (delegate* unmanaged[Cdecl]<nint, void>)_export("alcDestroyContext");
		alcGetError = (delegate* unmanaged[Cdecl]<nint, int>)_export("alcGetError");
		alcGetString = (delegate* unmanaged[Cdecl]<nint, int, byte*>)_export("alcGetString");
		alcIsExtensionPresent = (delegate* unmanaged[Cdecl]<nint, byte*, byte>)_export("alcIsExtensionPresent");
		alcGetIntegerv = (delegate* unmanaged[Cdecl]<nint, int, int, int*, void>)_export("alcGetIntegerv");

		alGetError = (delegate* unmanaged[Cdecl]<int>)_export("alGetError");
		alIsExtensionPresent = (delegate* unmanaged[Cdecl]<byte*, byte>)_export("alIsExtensionPresent");
		alGenSources = (delegate* unmanaged[Cdecl]<int, uint*, void>)_export("alGenSources");
		alDeleteSources = (delegate* unmanaged[Cdecl]<int, uint*, void>)_export("alDeleteSources");
		alGenBuffers = (delegate* unmanaged[Cdecl]<int, uint*, void>)_export("alGenBuffers");
		alDeleteBuffers = (delegate* unmanaged[Cdecl]<int, uint*, void>)_export("alDeleteBuffers");
		alBufferData = (delegate* unmanaged[Cdecl]<uint, int, void*, int, int, void>)_export("alBufferData");
		alSourceQueueBuffers = (delegate* unmanaged[Cdecl]<uint, int, uint*, void>)_export("alSourceQueueBuffers");
		alSourceUnqueueBuffers = (delegate* unmanaged[Cdecl]<uint, int, uint*, void>)_export("alSourceUnqueueBuffers");
		alGetSourcei = (delegate* unmanaged[Cdecl]<uint, int, int*, void>)_export("alGetSourcei");
		alSourcei = (delegate* unmanaged[Cdecl]<uint, int, int, void>)_export("alSourcei");
		alSourcef = (delegate* unmanaged[Cdecl]<uint, int, float, void>)_export("alSourcef");
		alSourcePlay = (delegate* unmanaged[Cdecl]<uint, void>)_export("alSourcePlay");
		alSourceStop = (delegate* unmanaged[Cdecl]<uint, void>)_export("alSourceStop");
	}

	/// <summary>The library that was loaded.</summary>
	public string LibraryName { get; }

	/// <summary>
	/// The libraries tried, in order, on this platform: OpenAL Soft's name first, then the system library.
	/// </summary>
	public static IReadOnlyList<string> Candidates
	{
		get
		{
			if (OperatingSystem.IsWindows()) return ["soft_oal.dll", "OpenAL32.dll"];
			if (OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst())
				return ["libopenal.dylib", "/System/Library/Frameworks/OpenAL.framework/OpenAL"];
			if (OperatingSystem.IsMacOS())
				return ["libopenal.dylib", "libopenal.1.dylib", "/System/Library/Frameworks/OpenAL.framework/OpenAL"];
			if (OperatingSystem.IsAndroid()) return ["libopenal.so"];
			return ["libopenal.so", "libopenal.so.1"];
		}
	}

	/// <summary>
	/// Loads the first OpenAL library found (see <see cref="Candidates"/>).
	/// </summary>
	/// <exception cref="DllNotFoundException">No OpenAL library could be loaded.</exception>
	public static OpenAlNative Load()
	{
		foreach (var name in Candidates)
		{
			if (!_tryLoad(name, out var handle)) continue;

			try
			{
				return new OpenAlNative(handle, name);
			}
			catch (EntryPointNotFoundException)
			{
				NativeLibrary.Free(handle);
			}
		}

		throw new DllNotFoundException($"No OpenAL library was found (tried {string.Join(", ", Candidates)}). Ship OpenAL Soft with the game (Silk.NET.OpenAL.Soft.Native for desktop, a libopenal.so built for Android) or install it.");
	}

	private static bool _tryLoad(string name, out nint handle)
	{
		// Rooted paths load as is; bare names also search the app's native asset folders (runtimes/<rid>/native).
		if (Path.IsPathRooted(name)) return NativeLibrary.TryLoad(name, out handle);
		return NativeLibrary.TryLoad(name, typeof(OpenAlNative).Assembly, DllImportSearchPath.SafeDirectories | DllImportSearchPath.ApplicationDirectory, out handle)
			|| NativeLibrary.TryLoad(name, out handle);
	}

	private nint _export(string name) => NativeLibrary.GetExport(_library, name);

	public bool IsAlExtensionPresent(string name)
	{
		var bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
		fixed (byte* p = bytes) return alIsExtensionPresent(p) != 0;
	}

	public bool IsAlcExtensionPresent(nint device, string name)
	{
		var bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
		fixed (byte* p = bytes) return alcIsExtensionPresent(device, p) != 0;
	}

	public string? AlcString(nint device, int param)
	{
		var p = alcGetString(device, param);
		return p is null ? null : Marshal.PtrToStringUTF8((nint)p);
	}

	/// <summary>Reads a list of NUL-separated strings ending with an empty string (device lists).</summary>
	public List<string> AlcStringList(nint device, int param)
	{
		var names = new List<string>();
		var p = alcGetString(device, param);
		if (p is null) return names;

		while (*p != 0)
		{
			var length = 0;
			while (p[length] != 0) length++;
			names.Add(System.Text.Encoding.UTF8.GetString(p, length));
			p += length + 1;
		}

		return names;
	}

	public void Dispose()
	{
		if (_library == 0) return;
		NativeLibrary.Free(_library);
		_library = 0;
	}
}
