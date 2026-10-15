using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ion.Extensions.Assets;

/// <summary>
/// The default <see cref="IAssetWatcher"/>: a <see cref="FileSystemWatcher"/> over the assets root (subfolders included)
/// that queues the relative paths of created, changed and renamed files. Registered by
/// <see cref="BuilderExtensions.AddAssets"/>.
/// </summary>
/// <remarks>
/// A file must go <see cref="SettleTime"/> without further change events before <see cref="Drain"/> returns it, so an editor
/// that writes a file in several steps triggers one reload of the finished file. Paths queued with <see cref="Enqueue"/> are
/// ready immediately. The file system events arrive on thread pool threads; the queue is thread-safe.
/// </remarks>
public sealed class AssetWatcher : IAssetWatcher, IDisposable
{
	/// <summary>The configuration key that turns hot reload on or off: <c>Ion:Assets:HotReload</c>.</summary>
	public const string HotReloadKey = "Ion:Assets:HotReload";

	/// <summary>
	/// Whether hot reload is on when <see cref="HotReloadKey"/> is not set: true in Debug builds of this package, false in
	/// Release builds.
	/// </summary>
#if DEBUG
	public const bool HotReloadDefault = true;
#else
	public const bool HotReloadDefault = false;
#endif

	private readonly ConcurrentDictionary<string, long> _pending = new(StringComparer.Ordinal);
	private readonly FileSystemWatcher? _watcher;
	private readonly ILogger _logger;

	/// <summary>
	/// Creates a watcher over <paramref name="root"/>. When <paramref name="enabled"/> is false, or the folder does not
	/// exist, no file system watcher runs and only <see cref="Enqueue"/> queues reloads.
	/// </summary>
	public AssetWatcher(string root, bool enabled, ILogger<AssetWatcher>? logger = null)
	{
		ArgumentNullException.ThrowIfNull(root);
		Root = Path.GetFullPath(root);
		_logger = (ILogger?)logger ?? NullLogger.Instance;

		if (!enabled) return;

		if (!Directory.Exists(Root))
		{
			_logger.LogDebug("Asset hot reload is off: the assets folder '{AssetsRoot}' does not exist.", Root);
			return;
		}

		try
		{
			_watcher = new FileSystemWatcher(Root)
			{
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
			};
			_watcher.Changed += _onChanged;
			_watcher.Created += _onChanged;
			_watcher.Renamed += _onRenamed;
			_watcher.Error += _onError;
			_watcher.EnableRaisingEvents = true;
			_logger.LogInformation("Asset hot reload: watching '{AssetsRoot}'.", Root);
		}
		catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException)
		{
			_watcher?.Dispose();
			_watcher = null;
			_logger.LogWarning(ex, "Asset hot reload is off: cannot watch '{AssetsRoot}'.", Root);
		}
	}

	/// <inheritdoc/>
	public bool IsEnabled => _watcher is not null;

	/// <inheritdoc/>
	public string Root { get; }

	/// <summary>
	/// How long a changed file must go without further change events before it is reloaded. Defaults to 100 ms.
	/// </summary>
	public TimeSpan SettleTime { get; set; } = TimeSpan.FromMilliseconds(100);

	/// <summary>
	/// Whether hot reload is on for <paramref name="configuration"/>: <see cref="HotReloadKey"/> when set to a boolean,
	/// otherwise <see cref="HotReloadDefault"/>.
	/// </summary>
	public static bool IsHotReloadEnabled(IConfiguration? configuration)
	{
		return bool.TryParse(configuration?[HotReloadKey], out var enabled) ? enabled : HotReloadDefault;
	}

	/// <inheritdoc/>
	public void Enqueue(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		_pending[AssetPaths.Normalize(path)] = long.MinValue;
	}

	/// <inheritdoc/>
	public int Drain(ICollection<string> paths)
	{
		ArgumentNullException.ThrowIfNull(paths);
		if (_pending.IsEmpty) return 0;

		var settleTicks = (long)(SettleTime.TotalSeconds * Stopwatch.Frequency);
		var now = Stopwatch.GetTimestamp();
		var count = 0;
		foreach (var (path, changedAt) in _pending)
		{
			if (changedAt != long.MinValue && now - changedAt < settleTicks) continue;

			// Only remove the entry if no newer change arrived meanwhile.
			if (_pending.TryRemove(new KeyValuePair<string, long>(path, changedAt)))
			{
				paths.Add(path);
				count++;
			}
		}

		return count;
	}

	/// <summary>
	/// Stops watching.
	/// </summary>
	public void Dispose()
	{
		_watcher?.Dispose();
	}

	private void _onChanged(object sender, FileSystemEventArgs e) => _queue(e.FullPath);

	private void _onRenamed(object sender, RenamedEventArgs e) => _queue(e.FullPath);

	private void _onError(object sender, ErrorEventArgs e) => _logger.LogWarning(e.GetException(), "Asset hot reload: the file system watcher reported an error; some changes may be missed.");

	private void _queue(string fullPath)
	{
		if (Directory.Exists(fullPath)) return;

		var relative = Path.GetRelativePath(Root, fullPath);
		if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return;

		_pending[AssetPaths.Normalize(relative)] = Stopwatch.GetTimestamp();
	}
}
