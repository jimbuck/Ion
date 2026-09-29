using System.Diagnostics;
using System.Text;

using Ion.Testing;

namespace Ion.Tools.Tests;

/// <summary>Paths of the repository and of the built tool, and a way to run the tool as a process.</summary>
internal static class Repo
{
	private static readonly Lazy<string> RootPath = new(() =>
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
		{
			if (File.Exists(Path.Combine(dir.FullName, "Ion.sln"))) return dir.FullName;
		}

		throw new InvalidOperationException("Ion.sln not found above the test output.");
	});

	public static string Root => RootPath.Value;

	/// <summary>The configuration the tests were built with (Debug or Release).</summary>
	public static string Configuration => AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Release" : "Debug";

	/// <summary>The built ion tool.</summary>
	public static string ToolDll => Path.Combine(Root, "Ion", "Ion.Tools", "bin", Configuration, "net10.0", "Ion.Tools.dll");

	/// <summary>The Breakout sample (keeps the remote module in Release: IonRemote=true).</summary>
	public static string Breakout => Path.Combine(Root, "Ion.Examples", "Ion.Examples.Breakout", "Ion.Examples.Breakout.csproj");

	/// <summary>Whether headless rendering works here (a Vulkan or EGL driver).</summary>
	public static bool CanRender => RenderingEnvironment.HasVulkan || RenderingEnvironment.HasHeadlessGles;

	/// <summary>A fresh temporary directory.</summary>
	public static string TempDirectory(string name)
	{
		var path = Path.Combine(Path.GetTempPath(), "ion-tools-tests", $"{name}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	/// <summary>Runs the ion tool with <paramref name="args"/>; returns its exit code and output.</summary>
	public static (int ExitCode, string Output) Ion(string workingDirectory, params string[] args)
	{
		var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
		start.ArgumentList.Add(ToolDll);
		foreach (var arg in args) start.ArgumentList.Add(arg);
		start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
		start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
		using var process = Process.Start(start)!;
		var output = new StringBuilder();
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
		{
			process.Kill(entireProcessTree: true);
			throw new TimeoutException($"ion {string.Join(' ', args)} did not finish:\n{output}");
		}

		process.WaitForExit();
		return (process.ExitCode, output.ToString());
	}
}

/// <summary>Tests that build or run games share the build output: they run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DotnetBuildCollection
{
	public const string Name = "dotnet build";
}

/// <summary>
/// A slow test (it builds the engine from source into a new game): runs only with <c>ION_SLOW_TESTS=1</c>.
/// </summary>
public sealed class SlowFactAttribute : FactAttribute
{
	public SlowFactAttribute()
	{
		if (Environment.GetEnvironmentVariable("ION_SLOW_TESTS") is not ("1" or "true")) Skip = "Slow: builds the engine from source into generated games. Set ION_SLOW_TESTS=1 to run.";
	}
}
