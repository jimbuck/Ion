using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ion;

/// <summary>
/// Raises the Windows system timer resolution to 1 ms (<c>timeBeginPeriod(1)</c>) for the lifetime of a scope, so that
/// <see cref="Thread.Sleep(int)"/> wakes within about a millisecond instead of the default 15.6 ms. A no-op elsewhere.
/// </summary>
internal static partial class WindowsTimerResolution
{
	private const uint PeriodMs = 1;

	public static Scope Begin()
	{
		if (!OperatingSystem.IsWindows()) return default;

		try
		{
			return new Scope(TimeBeginPeriod(PeriodMs) == 0);
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
			return default;
		}
	}

	internal readonly struct Scope(bool active) : IDisposable
	{
		public bool IsActive { get; } = active;

		public void Dispose()
		{
			if (IsActive && OperatingSystem.IsWindows()) TimeEndPeriod(PeriodMs);
		}
	}

	[SupportedOSPlatform("windows")]
	[LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
	private static partial uint TimeBeginPeriod(uint uPeriod);

	[SupportedOSPlatform("windows")]
	[LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
	private static partial uint TimeEndPeriod(uint uPeriod);
}
