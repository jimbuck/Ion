using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Remote;

/// <summary>
/// Keeps the last log entries of the application for <c>log.tail</c>: an <see cref="ILoggerProvider"/> writing into a
/// bounded ring. Thread safe; each entry has an increasing sequence number so a client can ask for what is new.
/// </summary>
public sealed class RemoteLogBuffer : ILoggerProvider
{
	/// <summary>The number of entries kept.</summary>
	public const int Capacity = 2048;

	private readonly Lock _lock = new();
	private readonly RemoteLogEntry[] _entries = new RemoteLogEntry[Capacity];
	private long _next;

	/// <summary>The sequence number the next entry will get (entries so far).</summary>
	public long Next
	{
		get
		{
			lock (_lock) return _next;
		}
	}

	/// <summary>Adds an entry.</summary>
	public void Add(LogLevel level, string category, string message, string? exception)
	{
		lock (_lock)
		{
			_entries[_next % Capacity] = new RemoteLogEntry(_next, DateTimeOffset.UtcNow, level, category, message, exception);
			_next++;
		}
	}

	/// <summary>The entries with a sequence number of at least <paramref name="since"/> and a level of at least <paramref name="minLevel"/>, at most <paramref name="limit"/> (the newest).</summary>
	public List<RemoteLogEntry> Tail(long since, LogLevel minLevel, int limit)
	{
		var result = new List<RemoteLogEntry>();
		lock (_lock)
		{
			var first = Math.Max(since, Math.Max(0, _next - Capacity));
			for (var seq = first; seq < _next; seq++)
			{
				var entry = _entries[seq % Capacity];
				if (entry.Level >= minLevel) result.Add(entry);
			}
		}

		if (result.Count > limit) result.RemoveRange(0, result.Count - limit);
		return result;
	}

	/// <inheritdoc/>
	public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

	/// <inheritdoc/>
	public void Dispose()
	{
	}

	private sealed class Logger(RemoteLogBuffer buffer, string category) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (!IsEnabled(logLevel)) return;
			buffer.Add(logLevel, category, formatter(state, exception), exception?.ToString());
		}
	}
}

/// <summary>One captured log entry.</summary>
public readonly record struct RemoteLogEntry(long Seq, DateTimeOffset Time, LogLevel Level, string Category, string Message, string? Exception);
