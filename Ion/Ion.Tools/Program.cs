using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Ion.Tools;

return Cli.Execute(args);

namespace Ion.Tools
{
	/// <summary>The <c>ion</c> command line.</summary>
	internal static class Cli
	{
		public const string Usage = """
			ion: the Ion engine command line.

			  ion new <2d|3d|ecs> [name] [--output <dir>] [--ion-source <repo>] [--force]
			      Creates a game from a template (a game project, a test project with a headless test and a snapshot
			      test, CLAUDE.md, appsettings.json). --ion-source builds against an Ion source checkout.
			  ion run [project] [--headless] [--render] [--frames N] [--seed S] [--screenshot file.png] [--summary file.json]
			          [--remote] [--remote-allow-mutations] [--pause-at N] [-c Debug|Release] [-- game args]
			      Builds and runs the game. Exits with the game's exit code (non-zero when it threw). The summary has frame
			      stats, counters, warnings, errors, the exception and the schedule.
			  ion schedule [project] [-c config]
			      Prints the game's schedule (every stage's steps in run order, orders and scopes).
			  ion bench [filter] [--project <benchmarks project>] [-- BenchmarkDotNet args]
			      Runs the benchmarks whose names match the filter (default all) in Release.
			  ion trace [project] [--frames N] [--out trace.json] [--windowed]
			      Runs N frames (default 300) with profiling on and writes a Chrome trace (open in ui.perfetto.dev).
			  ion diff <actual.png> <expected.png> [--tolerance N] [--max-ratio R] [--out diff.png]
			      Compares two PNGs (exit code 0 when they match, 1 otherwise) and writes a diff image.
			  ion remote <method> [params-json] [--project <dir> | --token-file <file>]
			      Calls a remote protocol method of a game running with --remote (ion remote rpc.discover lists them).
			  ion publish [project] --target <preset> [-c Release] [--output <dir>] [--sysroot <dir>] [-- msbuild args]
			      Publishes the game with a NativeAOT preset: win-x64, win-arm64, osx-arm64, osx-x64, linux-x64, linux-arm64
			      or r36s (the R36S handheld: linux-arm64, OpenGL ES, SDL, fullscreen 640x480, plus the ArkOS ports layout
			      in <output>-arkos). Same as dotnet publish -p:IonTarget=<preset>; --sysroot sets IonArm64SysRoot.
			  ion mcp
			      Serves the Model Context Protocol on stdio for coding agents (claude mcp add ion -- ion mcp).
			""";

		public static int Execute(string[] args)
		{
			if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
			{
				Console.Out.Write(Usage);
				return args.Length == 0 ? 1 : 0;
			}

			if (args[0] is "--version")
			{
				Console.Out.WriteLine(typeof(Cli).Assembly.GetName().Version?.ToString(3));
				return 0;
			}

			try
			{
				var rest = args[1..];
				return args[0] switch
				{
					"new" => New(new Arguments(rest)),
					"run" => Run(new Arguments(rest)),
					"schedule" => Schedule(new Arguments(rest)),
					"bench" => Bench(new Arguments(rest)),
					"trace" => Trace(new Arguments(rest)),
					"diff" => Diff(new Arguments(rest)),
					"remote" => Remote(new Arguments(rest)),
					"publish" => Publish(new Arguments(rest)),
					"mcp" => Mcp(),
					_ => Fail($"Unknown command '{args[0]}'.\n\n{Usage}"),
				};
			}
			catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or FormatException or JsonException or UnauthorizedAccessException or InvalidDataException or HttpRequestException)
			{
				return Fail(ex.Message);
			}
			catch (RemoteCallException ex)
			{
				return Fail($"error {ex.Code.ToString(CultureInfo.InvariantCulture)}: {ex.Message}");
			}
		}

		private static int Fail(string message)
		{
			Console.Error.WriteLine($"ion: {message}");
			return 2;
		}

		private static int New(Arguments a)
		{
			var kind = a.Positional(0) ?? throw new ArgumentException("ion new needs a template: 2d, 3d or ecs.");
			var name = a.Positional(1) ?? (a.Option("--output") is { } dir ? Path.GetFileName(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar)) : "MyGame");
			var output = a.Option("--output") ?? name;
			var written = Templates.Write(kind, name, output, a.Option("--ion-source"), a.Flag("--force"));
			var root = Path.GetFullPath(output);
			Console.Out.WriteLine($"Created the {kind} game '{name}' in {root} ({written.Count} files).");
			Console.Out.WriteLine($"  cd {output}");
			Console.Out.WriteLine($"  ion run --headless --frames 600 --screenshot out/frame600.png --summary out/run.json");
			Console.Out.WriteLine($"  dotnet test");
			Console.Out.WriteLine("Read CLAUDE.md for the engine workflow.");
			return 0;
		}

		private static int Run(Arguments a)
		{
			var options = new GameRunOptions
			{
				Project = a.Positional(0),
				Configuration = a.Option("-c") ?? a.Option("--configuration") ?? "Debug",
				Headless = a.Flag("--headless"),
				Render = a.Flag("--render"),
				Frames = a.IntOption("--frames"),
				Seed = a.IntOption("--seed"),
				Screenshot = a.Option("--screenshot"),
				Summary = a.Option("--summary"),
				Remote = a.Flag("--remote"),
				AllowMutations = a.Flag("--remote-allow-mutations"),
				PauseAtFrame = a.IntOption("--pause-at"),
				RunDirectory = a.Option("--run-dir"),
				ExtraArgs = a.Rest,
			};
			a.ThrowOnUnknown();
			return GameRunner.Run(options);
		}

		private static int Schedule(Arguments a)
		{
			var options = new GameRunOptions
			{
				Project = a.Positional(0),
				Configuration = a.Option("-c") ?? a.Option("--configuration") ?? "Debug",
				Headless = true,
				Frames = 0,
				ExtraArgs = ["--Ion:PrintSchedule=true", "--Logging:LogLevel:Default=Warning", .. a.Rest],
			};
			a.ThrowOnUnknown();
			return GameRunner.Run(options);
		}

		private static int Trace(Arguments a)
		{
			var frames = a.IntOption("--frames") ?? 300;
			var output = Path.GetFullPath(a.Option("--out") ?? "trace.json");
			var options = new GameRunOptions
			{
				Project = a.Positional(0),
				Configuration = a.Option("-c") ?? a.Option("--configuration") ?? "Debug",
				Headless = !a.Flag("--windowed"),
				Frames = frames,
				ExtraArgs =
				[
					"--Ion:Metrics:Profiling=true",
					$"--Ion:Metrics:TraceOutput={output}",
					$"--Ion:Metrics:HistoryFrames={Math.Max(frames, 1).ToString(CultureInfo.InvariantCulture)}",
					.. a.Rest,
				],
			};
			a.ThrowOnUnknown();
			var code = GameRunner.Run(options);
			if (code == 0) Console.Out.WriteLine(File.Exists(output) ? $"ion: trace written to {output} (open it in https://ui.perfetto.dev)" : $"ion: the game did not write {output} (is the metrics module installed? AddIon does it).");
			return code;
		}

		private static int Bench(Arguments a)
		{
			var project = a.Option("--project") ?? FindBenchmarks(Directory.GetCurrentDirectory())
				?? throw new FileNotFoundException("No *.Benchmarks project found here or above; pass --project.");
			var filter = a.Positional(0) ?? "*";
			if (!filter.Contains('*', StringComparison.Ordinal)) filter = $"*{filter}*";
			a.ThrowOnUnknown();
			var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false };
			foreach (var arg in (string[])["run", "-c", "Release", "--project", Path.GetFullPath(project), "--", "--filter", filter, .. a.Rest]) start.ArgumentList.Add(arg);
			using var process = System.Diagnostics.Process.Start(start)!;
			process.WaitForExit();
			return process.ExitCode;
		}

		private static string? FindBenchmarks(string directory)
		{
			for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent)
			{
				var found = Directory.EnumerateFiles(dir.FullName, "*.Benchmarks.csproj", SearchOption.AllDirectories)
					.Where(static p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
					.Order(StringComparer.Ordinal).FirstOrDefault();
				if (found is not null) return found;
				if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) break;
			}

			return null;
		}

		private static int Diff(Arguments a)
		{
			var actual = a.Positional(0) ?? throw new ArgumentException("ion diff needs the actual PNG.");
			var expected = a.Positional(1) ?? throw new ArgumentException("ion diff needs the expected (golden) PNG.");
			var tolerance = a.IntOption("--tolerance") ?? 2;
			var maxRatio = a.Option("--max-ratio") is { } r ? double.Parse(r, CultureInfo.InvariantCulture) : 0;
			var output = a.Option("--out") ?? Path.ChangeExtension(actual, ".diff.png");
			a.ThrowOnUnknown();
			var result = ImageDiff.Compare(actual, expected, tolerance, output);
			var match = result.Matches(maxRatio);
			Console.Out.WriteLine(result.SameSize
				? $"{(match ? "match" : "MISMATCH")}: {result.MismatchedPixels} of {result.Width * result.Height} pixels differ by more than {tolerance} (max channel difference {result.MaxChannelDifference}); diff: {result.DiffPath}"
				: $"MISMATCH: sizes differ (actual {result.Width}x{result.Height}).");
			return match ? 0 : 1;
		}

		private static int Remote(Arguments a)
		{
			var method = a.Positional(0) ?? throw new ArgumentException("ion remote needs a method (ion remote rpc.discover lists them).");
			var parameters = a.Positional(1) is { } json ? JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException("params must be a JSON object.") : null;
			var tokenFile = a.Option("--token-file")
				?? Path.Combine(a.Option("--run-dir") ?? GameRunner.DefaultRunDirectory(GameRunner.ResolveProject(a.Option("--project"))), GameRunner.TokenFileName);
			a.ThrowOnUnknown();
			if (!File.Exists(tokenFile)) throw new FileNotFoundException($"No token file at {tokenFile}; is the game running with --remote?");
			using var client = RemoteClient.FromTokenFile(tokenFile);
			var result = client.Call(method, parameters);
			Console.Out.WriteLine(result?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null");
			return 0;
		}

		/// <summary>The publishing presets of <c>build/Ion.Publish.props</c> (the IonTarget values).</summary>
		public static readonly string[] PublishTargets = ["win-x64", "win-arm64", "osx-arm64", "osx-x64", "linux-x64", "linux-arm64", "r36s"];

		private static int Publish(Arguments a)
		{
			var target = a.Option("--target") ?? throw new ArgumentException($"ion publish needs --target <preset>: {string.Join(", ", PublishTargets)}.");
			if (Array.IndexOf(PublishTargets, target) < 0) throw new ArgumentException($"Unknown publish target '{target}'. The presets are {string.Join(", ", PublishTargets)}.");
			var project = GameRunner.ResolveProject(a.Positional(0));
			var arguments = PublishArguments(project, target, a.Option("-c") ?? a.Option("--configuration"), a.Option("--output") ?? a.Option("-o"), a.Option("--sysroot"), a.Rest);
			a.ThrowOnUnknown();
			var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false };
			foreach (var arg in arguments) start.ArgumentList.Add(arg);
			using var process = System.Diagnostics.Process.Start(start)!;
			process.WaitForExit();
			return process.ExitCode;
		}

		/// <summary>The <c>dotnet</c> arguments of <c>ion publish</c>: a <c>dotnet publish</c> with the IonTarget preset.</summary>
		/// <exception cref="ArgumentException">An unknown preset.</exception>
		public static List<string> PublishArguments(string project, string target, string? configuration, string? output, string? sysroot, IReadOnlyList<string> extra)
		{
			if (Array.IndexOf(PublishTargets, target) < 0) throw new ArgumentException($"Unknown publish target '{target}'. The presets are {string.Join(", ", PublishTargets)}.");
			List<string> arguments = ["publish", project, "-c", configuration ?? "Release", $"-p:IonTarget={target}"];
			if (output is not null) arguments.AddRange(["-o", Path.GetFullPath(output)]);
			if (sysroot is not null) arguments.Add($"-p:IonArm64SysRoot={Path.GetFullPath(sysroot)}");
			arguments.AddRange(extra);
			return arguments;
		}

		private static int Mcp()
		{
			using var server = new McpServer(Console.In, Console.Out);
			return server.Run();
		}
	}

	/// <summary>A small argument parser: positionals, <c>--option value</c>, flags, and everything after <c>--</c>.</summary>
	internal sealed class Arguments
	{
		private static readonly HashSet<string> Flags = ["--headless", "--render", "--remote", "--remote-allow-mutations", "--force", "--windowed"];

		private readonly List<string> _positionals = [];
		private readonly Dictionary<string, string?> _options = new(StringComparer.Ordinal);
		private readonly HashSet<string> _used = new(StringComparer.Ordinal);

		public Arguments(string[] args)
		{
			var rest = new List<string>();
			for (var i = 0; i < args.Length; i++)
			{
				var arg = args[i];
				if (arg == "--")
				{
					rest.AddRange(args[(i + 1)..]);
					break;
				}

				if (arg.StartsWith('-') && arg.Length > 1)
				{
					var eq = arg.IndexOf('=', StringComparison.Ordinal);
					if (eq > 0) _options[arg[..eq]] = arg[(eq + 1)..];
					else if (Flags.Contains(arg) || i + 1 >= args.Length) _options[arg] = null;
					else _options[arg] = args[++i];
					continue;
				}

				_positionals.Add(arg);
			}

			Rest = rest;
		}

		public IReadOnlyList<string> Rest { get; }

		public string? Positional(int index) => index < _positionals.Count ? _positionals[index] : null;

		public bool Flag(string name)
		{
			_used.Add(name);
			return _options.TryGetValue(name, out var value) && value is null or "true";
		}

		public string? Option(string name)
		{
			_used.Add(name);
			return _options.GetValueOrDefault(name);
		}

		public int? IntOption(string name) => Option(name) is { } value
			? int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw new FormatException($"{name} needs an integer, not '{value}'.")
			: null;

		public void ThrowOnUnknown()
		{
			var unknown = _options.Keys.Where(k => !_used.Contains(k)).ToList();
			if (unknown.Count > 0) throw new ArgumentException($"Unknown option(s): {string.Join(", ", unknown)}. Pass game arguments after '--'.");
		}
	}
}
