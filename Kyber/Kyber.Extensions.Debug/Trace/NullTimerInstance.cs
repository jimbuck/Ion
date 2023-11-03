using System.Runtime.CompilerServices;

namespace Kyber.Extensions.Debug;

internal struct NullTimerInstance : ITraceTimerInstance
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Then(string name) { }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Stop() { }
}
