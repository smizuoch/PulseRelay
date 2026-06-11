using Microsoft.Extensions.Logging;

namespace PulseRelay.App.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> that keeps the most recent log entries in memory for the
/// Diagnostics view. Register it alongside the console provider so the desktop app shows the
/// same lines the CLI probe prints. Bounded: oldest entries are dropped beyond capacity.
/// </summary>
public sealed class RingBufferLogSink : ILoggerProvider
{
    public const int DefaultCapacity = 2000;

    private readonly int _capacity;
    private readonly Queue<LogEntry> _entries;
    private readonly object _gate = new();

    public RingBufferLogSink(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        _capacity = capacity;
        _entries = new Queue<LogEntry>(capacity);
    }

    /// <summary>Fires on the logging thread, not the UI thread.</summary>
    public event EventHandler<LogEntry>? EntryAdded;

    public IReadOnlyList<LogEntry> GetSnapshot()
    {
        lock (_gate)
        {
            return [.. _entries];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public ILogger CreateLogger(string categoryName) => new RingBufferLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }

        EntryAdded?.Invoke(this, entry);
    }

    private sealed class RingBufferLogger : ILogger
    {
        private readonly RingBufferLogSink _sink;
        private readonly string _category;

        public RingBufferLogger(RingBufferLogSink sink, string category)
        {
            _sink = sink;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (exception is not null)
            {
                // Log text stays English with ASCII punctuation (see repo log policy).
                message = $"{message} - {exception.GetType().Name}: {exception.Message}";
            }

            _sink.Add(new LogEntry(DateTimeOffset.Now, logLevel, _category, message));
        }
    }
}
