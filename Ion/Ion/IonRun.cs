using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ion.Extensions.Graphics;
using Ion.Extensions.Metrics;

namespace Ion;

/// <summary>
/// The configuration keys of an agent or CI run (what <c>ion run</c> passes): a fixed number of frames, a deterministic
/// clock, a screenshot of the last frame and a JSON summary. <c>AddIon</c>/<c>UseIon</c> honour them in every game.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term><c>Ion:Run:Frames</c></term><description>Run this many frames, then Destroy and exit (<see cref="IonApplication.RunFramesKey"/>).</description></item>
/// <item><term><c>Ion:Run:FixedStep</c></term><description>Use a <see cref="FixedStepClock"/> (every frame exactly <see cref="IonRun.FrameTime"/>). Default: on when headless and <c>Ion:Run:Frames</c> is set.</description></item>
/// <item><term><c>Ion:Run:Screenshot</c></term><description>Write the last rendered frame to this PNG at the end of the run (needs headless rendering or a window).</description></item>
/// <item><term><c>Ion:Run:Summary</c></term><description>Write the run summary JSON here at the end of the run, also when an exception ends it.</description></item>
/// <item><term><c>Ion:Seed</c></term><description>The random seed; games read it (the summary records it).</description></item>
/// </list>
/// </remarks>
public static class IonRun
{
	/// <summary>The fixed-step clock setting.</summary>
	public const string FixedStepKey = "Ion:Run:FixedStep";

	/// <summary>The screenshot path setting.</summary>
	public const string ScreenshotKey = "Ion:Run:Screenshot";

	/// <summary>The summary path setting.</summary>
	public const string SummaryKey = "Ion:Run:Summary";

	/// <summary>The seed setting.</summary>
	public const string SeedKey = "Ion:Seed";

	/// <summary>The frame time of the deterministic clock: one 60 Hz step rounded up to a whole tick (one fixed step per frame).</summary>
	public static readonly TimeSpan FrameTime = TimeSpan.FromTicks((TimeSpan.TicksPerSecond + 59) / 60);

	/// <summary>The seed configured with <c>Ion:Seed</c>, or <paramref name="fallback"/>.</summary>
	public static int Seed(IConfiguration config, int fallback = 0) =>
		int.TryParse(config[SeedKey], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed) ? seed : fallback;

	internal static bool UsesFixedStep(IConfiguration config, bool headless)
	{
		if (bool.TryParse(config[FixedStepKey], out var fixedStep)) return fixedStep;
		return headless && !string.IsNullOrEmpty(config[IonApplication.RunFramesKey]);
	}

	internal static bool WantsReport(IConfiguration config) =>
		!string.IsNullOrEmpty(config[ScreenshotKey]) || !string.IsNullOrEmpty(config[SummaryKey]);
}

/// <summary>Collects warnings and errors for the run summary.</summary>
internal sealed class RunLogCollector : ILoggerProvider
{
	private readonly Lock _lock = new();
	private readonly List<(LogLevel Level, string Category, string Message)> _entries = [];

	public const int Capacity = 200;

	public int Dropped { get; private set; }

	public List<(LogLevel Level, string Category, string Message)> Snapshot()
	{
		lock (_lock) return [.. _entries];
	}

	public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

	public void Dispose()
	{
	}

	private void Add(LogLevel level, string category, string message)
	{
		lock (_lock)
		{
			if (_entries.Count >= Capacity) Dropped++;
			else _entries.Add((level, category, message));
		}
	}

	private sealed class Logger(RunLogCollector owner, string category) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning && logLevel != LogLevel.None;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (!IsEnabled(logLevel)) return;
			var message = formatter(state, exception);
			if (exception is not null) message += $" ({exception.GetType().Name}: {exception.Message})";
			owner.Add(logLevel, category, message);
		}
	}
}

/// <summary>
/// Writes the run's screenshot and summary (see <see cref="IonRun"/>) when the game shuts down, and the summary with the
/// exception when an unhandled exception ends the run.
/// </summary>
public sealed class RunReportSystem : IFrameListener
{
	private readonly IServiceProvider _services;
	private readonly IConfiguration _config;
	private readonly RunLogCollector _logs;
	private readonly Stopwatch _wall = Stopwatch.StartNew();
	private readonly List<double> _frameMs = [];
	private long _frames;
	private double _workSum;
	private long _drawCalls;
	private long _sprites;
	private bool _written;
	private string? _screenshotPath;
	private string? _screenshotError;

	internal RunReportSystem(IServiceProvider services, IConfiguration config, RunLogCollector logs)
	{
		_services = services;
		_config = config;
		_logs = logs;
	}

	/// <summary>Starts collecting frame stats and hooks unhandled exceptions.</summary>
	[Init(Order = StageOrder.EngineSetupFirst)]
	public void Begin(GameTime dt)
	{
		_services.GetService<FrameProfiler>()?.AddListener(this);
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
	}

	/// <summary>Captures the last rendered frame and writes the summary, before any engine teardown.</summary>
	[Destroy(Order = StageOrder.EngineSetupFirst)]
	public void End(GameTime dt)
	{
		AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
		CaptureScreenshot();
		WriteSummary("ok", null);
	}

	/// <inheritdoc/>
	public void OnFrame(FrameProfile frame)
	{
		if (frame.Kind != FrameKind.Frame) return;
		_frames++;
		if (_frameMs.Count < 1_000_000) _frameMs.Add(frame.Stats.FrameMs);
		_workSum += frame.Stats.WorkMs;
		_drawCalls += frame.Stats.DrawCalls;
		_sprites += frame.Stats.Sprites;
	}

	private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		try
		{
			WriteSummary("exception", e.ExceptionObject as Exception);
		}
		catch (Exception)
		{
			// Nothing more can be done while the process terminates.
		}
	}

	private void CaptureScreenshot()
	{
		var path = _config[IonRun.ScreenshotKey];
		if (string.IsNullOrEmpty(path)) return;
		var source = _services.GetService<IScreenshotSource>();
		if (source is null)
		{
			_screenshotError = "No screenshot source: run headless with --Ion:Headless:Render=true (ion run --screenshot does this) or windowed.";
			return;
		}

		try
		{
			var full = Path.GetFullPath(path);
			var directory = Path.GetDirectoryName(full);
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
			source.SaveScreenshot(full);
			_screenshotPath = full;
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
		{
			_screenshotError = ex.Message;
		}
	}

	private void WriteSummary(string status, Exception? exception)
	{
		var path = _config[IonRun.SummaryKey];
		if (string.IsNullOrEmpty(path) || _written) return;
		_written = true;

		var buffer = new ArrayBufferWriter<byte>(4096);
		using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
		{
			json.WriteStartObject();
			json.WriteNumber("version", 1);
			json.WriteString("status", status);
			json.WriteString("title", _services.GetService<IOptions<GameConfig>>()?.Value.Title);
			json.WriteNumber("frames", _frames);
			if (int.TryParse(_config[IonApplication.RunFramesKey], NumberStyles.Integer, CultureInfo.InvariantCulture, out var requested)) json.WriteNumber("requestedFrames", requested);
			if (int.TryParse(_config[IonRun.SeedKey], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)) json.WriteNumber("seed", seed);
			json.WriteBoolean("headless", _config.IsHeadless());
			json.WriteBoolean("fixedStep", _services.GetService<IClock>() is FixedStepClock);
			json.WriteNumber("wallMs", Math.Round(_wall.Elapsed.TotalMilliseconds, 3));

			json.WriteStartObject("frameStats");
			json.WriteNumber("count", _frames);
			if (_frameMs.Count > 0)
			{
				var sorted = _frameMs.ToArray();
				Array.Sort(sorted);
				json.WriteNumber("avgFrameMs", Math.Round(sorted.Average(), 4));
				json.WriteNumber("minFrameMs", Math.Round(sorted[0], 4));
				json.WriteNumber("p95FrameMs", Math.Round(sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(sorted.Length * 0.95) - 1)], 4));
				json.WriteNumber("maxFrameMs", Math.Round(sorted[^1], 4));
				json.WriteNumber("avgWorkMs", Math.Round(_workSum / _frames, 4));
			}

			json.WriteNumber("totalDrawCalls", _drawCalls);
			json.WriteNumber("totalSprites", _sprites);
			json.WriteEndObject();

			if (_services.GetService<IMetrics>() is { } metrics)
			{
				json.WritePropertyName("lastFrame");
				json.WriteStartObject();
				var last = metrics.LastFrame;
				FrameLogWriter.WriteStats(json, in last);
				json.WriteEndObject();

				json.WriteStartObject("counters");
				foreach (var i in metrics.Instruments)
				{
					switch (i)
					{
						case MetricsCounter c: json.WriteNumber(c.Name, c.Value); break;
						case MetricsGauge g: json.WriteNumber(g.Name, g.Value); break;
						case MetricsHistogram h: json.WriteNumber(h.Name, h.TotalCount); break;
					}
				}

				json.WriteEndObject();
			}

			var logs = _logs.Snapshot();
			json.WriteStartArray("warnings");
			foreach (var (level, category, message) in logs)
			{
				if (level != LogLevel.Warning) continue;
				WriteLog(json, level, category, message);
			}

			if (_screenshotError is not null) WriteLog(json, LogLevel.Warning, "Ion.Run", $"Screenshot not written: {_screenshotError}");
			json.WriteEndArray();

			json.WriteStartArray("errors");
			foreach (var (level, category, message) in logs)
			{
				if (level < LogLevel.Error) continue;
				WriteLog(json, level, category, message);
			}

			json.WriteEndArray();

			if (exception is not null)
			{
				json.WriteStartObject("exception");
				json.WriteString("type", exception.GetType().FullName);
				json.WriteString("message", exception.Message);
				json.WriteString("stackTrace", exception.ToString());
				json.WriteEndObject();
			}
			else
			{
				json.WriteNull("exception");
			}

			if (_screenshotPath is not null) json.WriteString("screenshot", _screenshotPath);
			else json.WriteNull("screenshot");

			json.WriteString("schedule", _services.GetService<GameLoopContext>()?.Schedule?.Print());
			json.WriteEndObject();
		}

		var full = Path.GetFullPath(path);
		var directory = Path.GetDirectoryName(full);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		File.WriteAllBytes(full, buffer.WrittenSpan.ToArray());

		static void WriteLog(Utf8JsonWriter json, LogLevel level, string category, string message)
		{
			json.WriteStartObject();
			json.WriteString("level", level.ToString());
			json.WriteString("category", category);
			json.WriteString("message", message);
			json.WriteEndObject();
		}
	}
}
