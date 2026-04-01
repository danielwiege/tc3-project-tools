using Microsoft.Extensions.Logging;

namespace Tc3ProjectTools.Runtime;

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; init; }

    public LogLevel Level { get; init; }

    public string Category { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public sealed class UiLogStore
{
    private readonly object _sync = new();

    public IList<LogEntry> Entries { get; } = new List<LogEntry>();

    public event EventHandler<LogEntry>? EntryAdded;

    public void Add(LogEntry entry)
    {
        lock (_sync)
        {
            Entries.Add(entry);
            while (Entries.Count > 250)
            {
                Entries.RemoveAt(0);
            }
        }

        EntryAdded?.Invoke(this, entry);
    }
}

public sealed class UiLogStoreLoggerProvider : ILoggerProvider
{
    private readonly string _sessionLogPath;
    private readonly UiLogStore _store;
    private readonly object _writeLock = new();

    public UiLogStoreLoggerProvider(UiLogStore store, string sessionLogPath)
    {
        _store = store;
        _sessionLogPath = sessionLogPath;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new UiLogger(categoryName, _store, _sessionLogPath, _writeLock);
    }

    public void Dispose()
    {
    }

    private sealed class UiLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _sessionLogPath;
        private readonly UiLogStore _store;
        private readonly object _writeLock;

        public UiLogger(string categoryName, UiLogStore store, string sessionLogPath, object writeLock)
        {
            _categoryName = categoryName;
            _store = store;
            _sessionLogPath = sessionLogPath;
            _writeLock = writeLock;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var entry = new LogEntry
            {
                Timestamp = DateTimeOffset.Now,
                Level = logLevel,
                Category = _categoryName,
                Message = exception is null ? message : $"{message}{Environment.NewLine}{exception}",
            };

            _store.Add(entry);
            var line = $"{entry.Timestamp:O} [{entry.Level}] {_categoryName}: {entry.Message}{Environment.NewLine}";
            lock (_writeLock)
            {
                File.AppendAllText(_sessionLogPath, line);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
