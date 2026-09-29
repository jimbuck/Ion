using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ion.Tools;

/// <summary>How to run a game (what <c>ion run</c> and the MCP <c>ion_run</c> tool take).</summary>
public sealed record GameRunOptions
{
	/// <summary>The game project: a <c>.csproj</c>, or a directory containing one (or a template's solution directory).</summary>
	public string? Project { get; init; }

	/// <summary>The build configuration. Default Debug (Release builds compile the remote module out unless IonRemote=true).</summary>
	public string Configuration { get; init; } = "Debug";

	/// <summary>Run with the headless backends (<c>Ion:Headless</c>).</summary>
	public bool Headless { get; init; }

	/// <summary>Render headless into an offscreen target (<c>Ion:Headless:Render</c>); implied by <see cref="Screenshot"/> when headless.</summary>
	public bool Render { get; init; }

	/// <summary>Stop after this many frames (<c>Ion:Run:Frames</c>).</summary>
	public int? Frames { get; init; }

	/// <summary>The random seed (<c>Ion:Seed</c>).</summary>
	public int? Seed { get; init; }

	/// <summary>Write the last frame here as PNG (<c>Ion:Run:Screenshot</c>).</summary>
	public string? Screenshot { get; init; }

	/// <summary>Write the run summary JSON here (<c>Ion:Run:Summary</c>).</summary>
	public string? Summary { get; init; }

	/// <summary>Start the remote server (<c>--remote</c>, HTTP on a free loopback port).</summary>
	public bool Remote { get; init; }

	/// <summary>Grant the mutate scope (<c>--remote-allow-mutations</c>).</summary>
	public bool AllowMutations { get; init; }

	/// <summary>Pause after this many frames and keep serving remote requests (<c>Ion:Remote:PauseAtFrame</c>).</summary>
	public int? PauseAtFrame { get; init; }

	/// <summary>Extra arguments passed to the game.</summary>
	public IReadOnlyList<string> ExtraArgs { get; init; } = [];

	/// <summary>Where the token file and logs go. Default <c>&lt;project dir&gt;/.ion/run</c>.</summary>
	public string? RunDirectory { get; init; }
}

/// <summary>A game started with the remote server on, and a client connected to it.</summary>
public sealed class LiveGame : IDisposable
{
	private readonly StreamWriter _log;

	internal LiveGame(Process process, RemoteClient client, string runDirectory, string logPath, StreamWriter log)
	{
		_log = log;
		Process = process;
		Client = client;
		RunDirectory = runDirectory;
		LogPath = logPath;
	}

	/// <summary>The <c>dotnet run</c> process (the game is its child).</summary>
	public Process Process { get; }

	/// <summary>The remote client.</summary>
	public RemoteClient Client { get; }

	/// <summary>The run directory (token file and log).</summary>
	public string RunDirectory { get; }

	/// <summary>The game's standard output and error.</summary>
	public string LogPath { get; }

	/// <summary>Whether the game is still running.</summary>
	public bool IsRunning => !Process.HasExited;

	/// <summary>Asks the game to exit, then kills it (and its process tree) if it does not within <paramref name="timeout"/>.</summary>
	public int Stop(TimeSpan? timeout = null)
	{
		if (!Process.HasExited)
		{
			try
			{
				Client.Call("game.exit");
			}
			catch (Exception ex) when (ex is RemoteCallException or HttpRequestException or TaskCanceledException or IOException)
			{
			}

			if (!Process.WaitForExit(timeout ?? TimeSpan.FromSeconds(15))) Process.Kill(entireProcessTree: true);
		}

		// Waits for the output readers too, so the log is complete.
		Process.WaitForExit();
		lock (_log) _log.Flush();
		return Process.ExitCode;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		try
		{
			if (!Process.HasExited) Process.Kill(entireProcessTree: true);
			Process.WaitForExit();
		}
		catch (InvalidOperationException)
		{
		}

		lock (_log) _log.Dispose();
		Client.Dispose();
		Process.Dispose();
	}
}

/// <summary>
/// Runs Ion games for tools and agents: resolves the project, builds it, and runs it with <c>dotnet run</c> and the engine's
/// agent settings (<c>Ion:Run:*</c>, <c>Ion:Remote:*</c>), from the project directory.
/// </summary>
public static class GameRunner
{
	/// <summary>The token file name in the run directory.</summary>
	public const string TokenFileName = "remote.json";

	/// <summary>
	/// The game project for <paramref name="path"/>: a <c>.csproj</c> itself, the only non-test project in a directory, or
	/// the only non-test project one level down (a template's solution directory).
	/// </summary>
	/// <exception cref="FileNotFoundException">No project, or more than one candidate.</exception>
	public static string ResolveProject(string? path)
	{
		var full = Path.GetFullPath(string.IsNullOrEmpty(path) ? Directory.GetCurrentDirectory() : path);
		if (File.Exists(full) && full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return full;
		if (!Directory.Exists(full)) throw new FileNotFoundException($"No project or directory at '{full}'.");

		var candidates = Candidates(full);
		if (candidates.Count == 0)
		{
			candidates = [.. Directory.EnumerateDirectories(full).Where(static d => !Path.GetFileName(d).StartsWith('.')).SelectMany(Candidates)];
		}

		return candidates.Count switch
		{
			1 => candidates[0],
			0 => throw new FileNotFoundException($"No game project (.csproj, not *.Tests) in '{full}' or its subdirectories."),
			_ => throw new FileNotFoundException($"Several projects in '{full}': {string.Join(", ", candidates.Select(Path.GetFileName))}. Pass the one to run."),
		};

		static List<string> Candidates(string directory) => [.. Directory.EnumerateFiles(directory, "*.csproj")
			.Where(static p => !Path.GetFileNameWithoutExtension(p).EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
			.Order(StringComparer.Ordinal)];
	}

	/// <summary>The run directory of <paramref name="project"/>: <c>&lt;project dir&gt;/.ion/run</c>.</summary>
	public static string DefaultRunDirectory(string project) => Path.Combine(Path.GetDirectoryName(project)!, ".ion", "run");

	/// <summary>The arguments passed to the game for <paramref name="options"/> (absolute paths, configuration keys).</summary>
	public static List<string> GameArguments(GameRunOptions options, string runDirectory)
	{
		var args = new List<string>();
		if (options.Headless)
		{
			args.Add("--Ion:Headless=true");
			if (options.Render || options.Screenshot is not null) args.Add("--Ion:Headless:Render=true");
		}

		if (options.Frames is { } frames) args.Add($"--Ion:Run:Frames={frames.ToString(CultureInfo.InvariantCulture)}");
		if (options.Seed is { } seed) args.Add($"--Ion:Seed={seed.ToString(CultureInfo.InvariantCulture)}");
		if (options.Screenshot is { } screenshot) args.Add($"--Ion:Run:Screenshot={Path.GetFullPath(screenshot)}");
		if (options.Summary is { } summary) args.Add($"--Ion:Run:Summary={Path.GetFullPath(summary)}");
		if (!options.Headless && options.Screenshot is not null) args.Add("--Ion:Graphics:RetainLastFrame=true");
		if (options.Remote || options.AllowMutations || options.PauseAtFrame is not null)
		{
			args.Add("--Ion:Remote:Enabled=true");
			args.Add("--Ion:Remote:Port=0");
			args.Add($"--Ion:Remote:RunDirectory={runDirectory}");
			if (options.AllowMutations) args.Add("--Ion:Remote:AllowMutations=true");
			if (options.PauseAtFrame is { } pause)
			{
				args.Add($"--Ion:Remote:PauseAtFrame={pause.ToString(CultureInfo.InvariantCulture)}");
				// Headless: the deterministic clock, so "pause after N frames" reaches the same state every time, fast.
				if (options.Headless) args.Add("--Ion:Run:FixedStep=true");
			}
		}

		args.AddRange(options.ExtraArgs);
		return args;
	}

	/// <summary>Builds <paramref name="project"/>; returns the exit code and the build output.</summary>
	public static (int ExitCode, string Output) Build(string project, string configuration)
	{
		// No node reuse: a tool's build should not hand work to (or wait on) build nodes left over by other builds.
		var start = Dotnet(Path.GetDirectoryName(project)!, "build", project, "-c", configuration, "-nologo", "-v", "quiet", "-clp:NoSummary", "--disable-build-servers");
		start.RedirectStandardOutput = true;
		start.RedirectStandardError = true;
		using var process = Process.Start(start)!;
		var output = new StringBuilder();
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();
		return (process.ExitCode, output.ToString());
	}

	/// <summary>
	/// The start info for <c>dotnet run --no-build</c> of <paramref name="project"/> with <paramref name="gameArgs"/>, from the
	/// project directory.
	/// </summary>
	public static ProcessStartInfo RunStartInfo(string project, string configuration, IEnumerable<string> gameArgs)
	{
		var start = Dotnet(Path.GetDirectoryName(project)!, "run", "--no-build", "--no-launch-profile", "--project", project, "-c", configuration, "--");
		foreach (var arg in gameArgs) start.ArgumentList.Add(arg);
		return start;
	}

	/// <summary>
	/// Builds and runs a game to completion. Output goes to <paramref name="output"/> (or the console when null). When a
	/// summary is requested, the game's exit code is added to it (and a summary is written for a crash that left none).
	/// Returns the game's exit code (non-zero when it threw), or the build's when the build failed.
	/// </summary>
	public static int Run(GameRunOptions options, TextWriter? output = null, TextWriter? error = null)
	{
		var project = ResolveProject(options.Project);
		var runDirectory = Path.GetFullPath(options.RunDirectory ?? DefaultRunDirectory(project));
		var (buildCode, buildOutput) = Build(project, options.Configuration);
		if (buildCode != 0)
		{
			(error ?? Console.Error).Write(buildOutput);
			(error ?? Console.Error).WriteLine($"ion: build of {Path.GetFileName(project)} failed with exit code {buildCode}.");
			if (options.Summary is not null) WriteCrashSummary(options.Summary, buildCode, "build-failed", buildOutput);
			return buildCode;
		}

		if (options.Summary is { } summaryPath && File.Exists(summaryPath)) File.Delete(summaryPath);

		var start = RunStartInfo(project, options.Configuration, GameArguments(options, runDirectory));
		var tail = new Queue<string>();
		if (output is not null || error is not null)
		{
			start.RedirectStandardOutput = true;
			start.RedirectStandardError = true;
		}

		using var process = Process.Start(start)!;
		if (start.RedirectStandardOutput)
		{
			process.OutputDataReceived += (_, e) => Forward(e.Data, output ?? Console.Out, tail);
			process.ErrorDataReceived += (_, e) => Forward(e.Data, error ?? Console.Error, tail);
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
		}

		process.WaitForExit();
		var exitCode = process.ExitCode;

		if (options.Summary is { } summary) CompleteSummary(summary, exitCode, string.Join('\n', tail));
		return exitCode;
	}

	/// <summary>
	/// Builds and starts a game with the remote server on (and the mutate scope when asked), waits for its token file and
	/// connects. The game's output goes to <c>game.log</c> in the run directory.
	/// </summary>
	/// <exception cref="InvalidOperationException">The build failed, or the game exited or did not start its server in time.</exception>
	public static LiveGame Launch(GameRunOptions options, TimeSpan? timeout = null)
	{
		var project = ResolveProject(options.Project);
		var runDirectory = Path.GetFullPath(options.RunDirectory ?? DefaultRunDirectory(project));
		Directory.CreateDirectory(runDirectory);
		var tokenFile = Path.Combine(runDirectory, TokenFileName);
		if (File.Exists(tokenFile)) File.Delete(tokenFile);

		var (buildCode, buildOutput) = Build(project, options.Configuration);
		if (buildCode != 0) throw new InvalidOperationException($"The build of {Path.GetFileName(project)} failed:\n{buildOutput}");

		var logPath = Path.Combine(runDirectory, "game.log");
		var log = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };
		var start = RunStartInfo(project, options.Configuration, GameArguments(options with { Remote = true }, runDirectory));
		start.RedirectStandardOutput = true;
		start.RedirectStandardError = true;
		start.RedirectStandardInput = true;
		var process = Process.Start(start)!;
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.WriteLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.WriteLine(e.Data); };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(2));
		while (DateTime.UtcNow < deadline)
		{
			if (process.HasExited)
			{
				process.WaitForExit();
				lock (log) log.Dispose();
				throw new InvalidOperationException($"The game exited with code {process.ExitCode} before its remote server started. Log: {logPath}\n{Tail(logPath)}");
			}

			if (File.Exists(tokenFile))
			{
				try
				{
					var client = RemoteClient.FromTokenFile(tokenFile);
					return new LiveGame(process, client, runDirectory, logPath, log);
				}
				catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
				{
					// Still being written.
				}
			}

			Thread.Sleep(100);
		}

		process.Kill(entireProcessTree: true);
		process.WaitForExit();
		lock (log) log.Dispose();
		throw new InvalidOperationException($"The game did not start its remote server in time. Log: {logPath}\n{Tail(logPath)}");
	}

	/// <summary>The last lines of a log file.</summary>
	public static string Tail(string path, int lines = 40)
	{
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var reader = new StreamReader(stream);
			var all = reader.ReadToEnd().Split('\n');
			return string.Join('\n', all.Skip(Math.Max(0, all.Length - lines)));
		}
		catch (IOException)
		{
			return "";
		}
	}

	private static void Forward(string? line, TextWriter writer, Queue<string> tail)
	{
		if (line is null) return;
		lock (tail)
		{
			writer.WriteLine(line);
			tail.Enqueue(line);
			if (tail.Count > 60) tail.Dequeue();
		}
	}

	private static void CompleteSummary(string path, int exitCode, string outputTail)
	{
		JsonObject summary;
		if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing)
		{
			summary = existing;
			if (exitCode != 0 && summary["status"]?.GetValue<string>() == "ok") summary["status"] = "failed";
		}
		else
		{
			summary = new JsonObject { ["version"] = 1, ["status"] = "crashed", ["frames"] = 0, ["output"] = outputTail };
		}

		summary["exitCode"] = exitCode;
		File.WriteAllText(path, summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
	}

	private static void WriteCrashSummary(string path, int exitCode, string status, string output)
	{
		var full = Path.GetFullPath(path);
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		var summary = new JsonObject { ["version"] = 1, ["status"] = status, ["exitCode"] = exitCode, ["frames"] = 0, ["output"] = output };
		File.WriteAllText(full, summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
	}

	private static ProcessStartInfo Dotnet(string workingDirectory, params string[] args)
	{
		var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory, UseShellExecute = false };
		foreach (var arg in args) start.ArgumentList.Add(arg);
		// Keep the child's console output plain for agents.
		start.Environment["DOTNET_NOLOGO"] = "1";
		start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
		// Build servers and reused MSBuild nodes would inherit redirected output pipes and outlive the command, so a
		// caller waiting for the end of the output would wait forever.
		start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
		start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
		return start;
	}
}
