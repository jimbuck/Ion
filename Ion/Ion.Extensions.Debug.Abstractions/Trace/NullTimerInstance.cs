using System.Runtime.CompilerServices;

namespace Ion.Extensions.Debug;

public struct NullTimerInstance : ITraceTimerInstance
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Then(string name) { }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Stop() { }
}
