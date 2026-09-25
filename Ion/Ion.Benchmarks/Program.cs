using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Ion.Benchmarks;

public static class Program
{
	// Usage:
	//   dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*'            (full run, slow but precise)
	//   dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*' --job short (quick run)
	//   dotnet run -c Release --project Ion/Ion.Benchmarks -- --list flat
	public static void Main(string[] args)
	{
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance.WithOptions(ConfigOptions.DisableOptimizationsValidator));
	}
}
