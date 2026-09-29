using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Ion.Benchmarks;

public static class Program
{
	// Usage:
	//   dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*'            (full run, slow but precise)
	//   dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*' --job short (quick run)
	//   dotnet run -c Release --project Ion/Ion.Benchmarks -- --list flat
	//   dotnet run -c Release --project Ion/Ion.Benchmarks -- --min-of-n sprites  (SpriteBatchBenchmarks, best of many
	//     interleaved runs in one process: robust on noisy machines, for A/B checks while optimizing)
	public static void Main(string[] args)
	{
		if (args is ["--min-of-n", "sprites", ..])
		{
			SpriteBatchBenchmarks.MinOfN();
			return;
		}

		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance.WithOptions(ConfigOptions.DisableOptimizationsValidator));
	}
}
