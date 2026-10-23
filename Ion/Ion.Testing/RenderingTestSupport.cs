using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion.Extensions.Graphics.GLES;
using Ion.Extensions.Graphics.Vulkan;

namespace Ion.Testing;

/// <summary>
/// What the machine running rendering tests offers, for skipping tests that need a GPU driver or a display.
/// </summary>
public static class RenderingEnvironment
{
	private static readonly Lazy<bool> Vulkan = new(VulkanDevice.IsAvailable);

	private static readonly Lazy<bool> Gles = new(GlesDevice.IsHeadlessAvailable);

	/// <summary>A Vulkan driver with a device (Mesa lavapipe on CI).</summary>
	public static bool HasVulkan => Vulkan.Value;

	/// <summary>EGL with an OpenGL ES 3 driver for headless contexts (Mesa llvmpipe on CI).</summary>
	public static bool HasHeadlessGles => Gles.Value;

	/// <summary>A display to open windows on (X11 or Wayland on Linux; always true on Windows and macOS).</summary>
	public static bool HasDisplay => !OperatingSystem.IsLinux()
		|| !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
		|| !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

	/// <summary>
	/// The path of a golden image in the <c>Golden</c> folder next to the calling source file (so updates written with
	/// <see cref="GoldenImage.UpdateVariable"/> land in the repository), or in the output folder when the sources are gone.
	/// </summary>
	public static string GoldenPath(string name, [CallerFilePath] string sourceFile = "")
	{
		var sourceDirectory = Path.GetDirectoryName(sourceFile);
		if (!string.IsNullOrEmpty(sourceDirectory) && Directory.Exists(sourceDirectory)) return Path.Combine(sourceDirectory, "Golden", name);
		return Path.Combine(AppContext.BaseDirectory, "Golden", name);
	}
}

/// <summary>
/// Collects error and critical log entries (Vulkan validation errors are logged as errors by the backend) so a test can
/// assert that a run was clean.
/// </summary>
public sealed class ErrorLog : ILoggerProvider, ILogger
{
	private readonly ConcurrentQueue<string> _errors = new();

	/// <summary>The errors logged so far.</summary>
	public IReadOnlyCollection<string> Errors => _errors;

	/// <inheritdoc/>
	public ILogger CreateLogger(string categoryName) => this;

	/// <inheritdoc/>
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	/// <inheritdoc/>
	public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

	/// <inheritdoc/>
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (IsEnabled(logLevel)) _errors.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception));
	}

	/// <summary>Throws when any error was logged.</summary>
	/// <exception cref="InvalidOperationException">Errors were logged; the message lists them.</exception>
	public void AssertClean()
	{
		if (!_errors.IsEmpty) throw new InvalidOperationException("Errors were logged:\n" + string.Join("\n", _errors));
	}

	/// <summary>Turns graphics validation on in <paramref name="host"/> and routes its error logs here.</summary>
	public IonTestHost Attach(IonTestHost host)
	{
		ArgumentNullException.ThrowIfNull(host);
		return host
			.WithConfiguration("Ion:Graphics:Validation", "true")
			.Configure(services => services.AddLogging(logging => logging.AddProvider(this)));
	}

	/// <inheritdoc/>
	public void Dispose() { }
}
