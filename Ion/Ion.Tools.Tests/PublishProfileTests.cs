using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using Ion.Tests;

namespace Ion.Tools.Tests;

/// <summary>
/// The publishing presets of <c>build/Ion.Publish.props</c> and <c>.targets</c> (what <c>ion publish --target</c> and
/// <c>dotnet publish -p:IonTarget=</c> use), checked by evaluating the Breakout ECS sample with MSBuild, and the ArkOS SD
/// card layout of the r36s preset, built from a stand-in publish folder (no NativeAOT compile needed).
/// </summary>
[Collection(DotnetBuildCollection.Name)]
[Trait(TestConstants.CATEGORY, TestConstants.INTEGRATION)]
public sealed class PublishProfileTests
{
	private const string SampleName = "Ion.Examples.Breakout.ECS";

	private static readonly string[] CommonProperties =
	[
		"RuntimeIdentifier", "SelfContained", "PublishAot", "IlcGenerateStackTraceData", "EventSourceSupport",
		"InvariantGlobalization", "StripSymbols", "TrimMode", "DebuggerSupport", "PublishReadyToRun", "IonTargetKind",
		"IonArkOSLayout", "IonDefaultGraphicsBackend", "IonDefaultWindowPlatform", "IonDefaultFullscreen", "IonDefaultWidth",
		"IonDefaultHeight", "LinkerFlavor",
	];

	private static string Sample => Path.Combine(Repo.Root, "Ion.Examples", SampleName, $"{SampleName}.csproj");

	public static TheoryData<string, string> Presets => new()
	{
		{ "win-x64", "win-x64" },
		{ "win-arm64", "win-arm64" },
		{ "osx-arm64", "osx-arm64" },
		{ "osx-x64", "osx-x64" },
		{ "linux-x64", "linux-x64" },
		{ "linux-arm64", "linux-arm64" },
		{ "r36s", "linux-arm64" },
	};

	[Theory]
	[MemberData(nameof(Presets))]
	public void EveryPresetIsASelfContainedTrimmedNativeAotPublish(string preset, string rid)
	{
		var p = GetProperties(Sample, [$"-p:IonTarget={preset}"], CommonProperties);

		Assert.Equal(rid, p["RuntimeIdentifier"]);
		Assert.Equal("true", p["SelfContained"]);
		Assert.Equal("true", p["PublishAot"]);
		Assert.Equal("false", p["PublishReadyToRun"]);
		Assert.Equal("true", p["IlcGenerateStackTraceData"]);
		Assert.Equal("true", p["InvariantGlobalization"]);
		Assert.Equal("true", p["StripSymbols"]);
		Assert.Equal("full", p["TrimMode"]);
		Assert.Equal("false", p["DebuggerSupport"]);

		var handheld = preset == "r36s";
		Assert.Equal(handheld ? "handheld" : "desktop", p["IonTargetKind"]);
		// EventSource (dotnet-trace, dotnet-counters) on desktop only.
		Assert.Equal(handheld ? "false" : "true", p["EventSourceSupport"]);
		Assert.Equal(handheld ? "true" : "", p["IonArkOSLayout"]);

		// Cross-compiling for linux-arm64 from a linux-x64 machine links with lld (docs/platforms/r36s.md).
		if (rid == "linux-arm64" && OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
		{
			Assert.Equal("lld", p["LinkerFlavor"]);
		}
	}

	[Fact]
	public void TheR36sPresetDefaultsToOpenGlesSdlAndFullscreen640x480()
	{
		var p = GetProperties(Sample, ["-p:IonTarget=r36s"], CommonProperties);

		Assert.Equal("OpenGLES", p["IonDefaultGraphicsBackend"]);
		Assert.Equal("Sdl", p["IonDefaultWindowPlatform"]);
		Assert.Equal("true", p["IonDefaultFullscreen"]);
		Assert.Equal("640", p["IonDefaultWidth"]);
		Assert.Equal("480", p["IonDefaultHeight"]);
	}

	[Fact]
	public void WithoutAPresetNothingChanges()
	{
		var p = GetProperties(Sample, [], ["RuntimeIdentifier", "PublishAot", "IonTargetKind", "InvariantGlobalization"]);

		Assert.Equal("", p["RuntimeIdentifier"]);
		Assert.NotEqual("true", p["PublishAot"]);
		Assert.Equal("", p["IonTargetKind"]);
	}

	[Fact]
	public void LibrariesIgnoreThePreset()
	{
		// IonTarget flows to project references as a global property; engine libraries must not become self-contained.
		var library = Path.Combine(Repo.Root, "Ion", "Ion", "Ion.csproj");
		var p = GetProperties(library, ["-p:IonTarget=r36s"], ["RuntimeIdentifier", "PublishAot", "IonPublishable"]);

		Assert.Equal("false", p["IonPublishable"]);
		Assert.Equal("", p["RuntimeIdentifier"]);
	}

	[Fact]
	public void AnUnknownPresetIsErrorIonpub001()
	{
		var (code, output) = Dotnet(["msbuild", Sample, "-t:_IonValidatePublishTarget", "-p:IonTarget=r35s", "-nologo"]);

		Assert.NotEqual(0, code);
		Assert.Contains("IONPUB001", output, StringComparison.Ordinal);
		Assert.Contains("r36s", output, StringComparison.Ordinal);
	}

	[Fact]
	public void TheR36sDefaultConfigurationIsWrittenFromTheTemplate()
	{
		var obj = Repo.TempDirectory("r36s-config") + Path.DirectorySeparatorChar;
		var (code, output) = Dotnet(["msbuild", Sample, "-t:_IonAddHandheldConfig", "-p:IonTarget=r36s", $"-p:IntermediateOutputPath={obj}", "-nologo"]);
		Assert.True(code == 0, output);

		using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(obj, "appsettings.r36s.json")));
		var ion = json.RootElement.GetProperty("Ion");
		var window = ion.GetProperty("Window");
		Assert.Equal("Sdl", window.GetProperty("Platform").GetString());
		Assert.True(window.GetProperty("Fullscreen").GetBoolean());
		Assert.Equal(640, window.GetProperty("Width").GetInt32());
		Assert.Equal(480, window.GetProperty("Height").GetInt32());
		Assert.False(window.GetProperty("ShowCursor").GetBoolean());
		Assert.Equal("OpenGLES", ion.GetProperty("Graphics").GetProperty("PreferredBackend").GetString());
	}

	[Fact]
	public void TheR36sLayoutIsAnArkOSPortsFolderWithTheLauncherAndReadme()
	{
		// A stand-in for the NativeAOT publish folder: the executable, symbols, the native libraries and the content.
		var root = Repo.TempDirectory("arkos");
		var publish = Path.Combine(root, "publish");
		Directory.CreateDirectory(Path.Combine(publish, "Assets"));
		File.WriteAllText(Path.Combine(publish, SampleName), "binary");
		File.WriteAllText(Path.Combine(publish, $"{SampleName}.dbg"), "symbols");
		File.WriteAllText(Path.Combine(publish, "libglfw.so.3"), "glfw");
		File.WriteAllText(Path.Combine(publish, "libSDL2-2.0.so"), "sdl");
		File.WriteAllText(Path.Combine(publish, "appsettings.json"), "{}");
		File.WriteAllText(Path.Combine(publish, "appsettings.r36s.json"), "{}");
		File.WriteAllText(Path.Combine(publish, "Assets", "tiles.png"), "png");
		var layout = Path.Combine(root, "sd");

		var (code, output) = Dotnet(["msbuild", Sample, "-t:IonArkOSLayout", "-p:IonTarget=r36s", $"-p:PublishDir={publish}{Path.DirectorySeparatorChar}",
			$"-p:IonArkOSDir={layout}", "-p:IonArkOSName=breakout", "-p:IonArkOSTitle=Ion Breakout", "-nologo"]);
		Assert.True(code == 0, output);

		// ports/breakout.sh next to ports/breakout/, and the README at the top.
		var script = Path.Combine(layout, "ports", "breakout.sh");
		var game = Path.Combine(layout, "ports", "breakout");
		Assert.True(File.Exists(script), output);
		Assert.True(File.Exists(Path.Combine(layout, "README.md")));

		string[] files = [.. Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(game, f).Replace('\\', '/')).Order(StringComparer.Ordinal)];
		Assert.Equal(["Assets/tiles.png", SampleName, "appsettings.json", "appsettings.r36s.json", "libSDL2-2.0.so"], files);

		var launcher = File.ReadAllText(script);
		Assert.StartsWith("#!/bin/bash\n", launcher, StringComparison.Ordinal);
		Assert.DoesNotContain('\r', launcher);
		Assert.DoesNotContain("@", launcher.Replace("\"$@\"", "", StringComparison.Ordinal), StringComparison.Ordinal);
		Assert.Contains("GAMEDIR=\"$(cd \"$(dirname \"$0\")/breakout\" && pwd)\"", launcher, StringComparison.Ordinal);
		Assert.Contains("export DOTNET_ENVIRONMENT=r36s", launcher, StringComparison.Ordinal);
		Assert.Contains($"\"./{SampleName}\" \"$@\"", launcher, StringComparison.Ordinal);
		Assert.Contains("libSDL2-2.0.so", launcher, StringComparison.Ordinal);

		var readme = File.ReadAllText(Path.Combine(layout, "README.md"));
		Assert.StartsWith("# Ion Breakout for the R36S (ArkOS)", readme, StringComparison.Ordinal);
		Assert.Contains("/roms/ports/breakout.sh", readme, StringComparison.Ordinal);
		Assert.Contains($"/roms/ports/breakout/{SampleName}", readme, StringComparison.Ordinal);

		if (!OperatingSystem.IsWindows())
		{
			Assert.True(File.GetUnixFileMode(script).HasFlag(UnixFileMode.UserExecute));
			Assert.True(File.GetUnixFileMode(Path.Combine(game, SampleName)).HasFlag(UnixFileMode.UserExecute));
		}

		// The launcher is valid shell.
		if (File.Exists("/bin/bash"))
		{
			var (syntax, errors) = Run("/bin/bash", ["-n", script]);
			Assert.True(syntax == 0, errors);
		}
	}

	[Fact]
	public void TheLayoutNeedsAPublishedExecutable()
	{
		var empty = Repo.TempDirectory("arkos-empty");
		var (code, output) = Dotnet(["msbuild", Sample, "-t:IonArkOSLayout", "-p:IonTarget=r36s", $"-p:PublishDir={empty}{Path.DirectorySeparatorChar}", "-nologo"]);

		Assert.NotEqual(0, code);
		Assert.Contains("IONPUB004", output, StringComparison.Ordinal);
	}

	private static Dictionary<string, string> GetProperties(string project, string[] globals, string[] names)
	{
		var (code, output) = Dotnet(["msbuild", project, .. globals, .. names.Select(n => $"-getProperty:{n}"), "-nologo"]);
		Assert.True(code == 0, output);

		using var json = JsonDocument.Parse(output[output.IndexOf('{', StringComparison.Ordinal)..]);
		return json.RootElement.GetProperty("Properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
	}

	private static (int ExitCode, string Output) Dotnet(string[] args) => Run("dotnet", args);

	private static (int ExitCode, string Output) Run(string file, string[] args)
	{
		var start = new ProcessStartInfo(file) { WorkingDirectory = Repo.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
		foreach (var arg in args) start.ArgumentList.Add(arg);
		start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
		start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
		using var process = Process.Start(start)!;
		var output = new StringBuilder();
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
		{
			process.Kill(entireProcessTree: true);
			throw new TimeoutException($"{file} {string.Join(' ', args)} did not finish:\n{output}");
		}

		process.WaitForExit();
		return (process.ExitCode, output.ToString());
	}
}
