namespace Kyber.Extensions.Debug;

public record struct TraceTiming(int Id, string Name, double Start, double Stop, int ThreadId)
{
	public double Duration => Stop - Start;
}
