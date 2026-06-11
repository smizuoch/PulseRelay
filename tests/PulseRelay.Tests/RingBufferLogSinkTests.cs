using Microsoft.Extensions.Logging;
using PulseRelay.App.Logging;
using Xunit;

namespace PulseRelay.Tests;

public class RingBufferLogSinkTests
{
    [Fact]
    public void Captures_logged_messages_with_category_and_level()
    {
        using var sink = new RingBufferLogSink();
        var logger = sink.CreateLogger("PulseRelay.Test.Category");

        logger.LogInformation("HR {Bpm} bpm", 81);

        var entry = Assert.Single(sink.GetSnapshot());
        Assert.Equal("PulseRelay.Test.Category", entry.Category);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("HR 81 bpm", entry.Message);
    }

    [Fact]
    public void Folds_exception_into_message()
    {
        using var sink = new RingBufferLogSink();
        var logger = sink.CreateLogger("cat");

        logger.LogWarning(new InvalidOperationException("boom"), "send failed");

        var entry = Assert.Single(sink.GetSnapshot());
        Assert.Contains("send failed", entry.Message);
        Assert.Contains("InvalidOperationException", entry.Message);
        Assert.Contains("boom", entry.Message);
    }

    [Fact]
    public void Exception_folding_stays_ascii()
    {
        using var sink = new RingBufferLogSink();
        var logger = sink.CreateLogger("cat");

        logger.LogWarning(new InvalidOperationException("boom"), "send failed");

        var entry = Assert.Single(sink.GetSnapshot());
        Assert.All(entry.Message, c => Assert.True(char.IsAscii(c), $"Non-ASCII char '{c}' in: {entry.Message}"));
    }

    [Fact]
    public void Drops_oldest_entries_beyond_capacity()
    {
        using var sink = new RingBufferLogSink(capacity: 3);
        var logger = sink.CreateLogger("cat");

        for (int i = 0; i < 5; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }

        var entries = sink.GetSnapshot();
        Assert.Equal(3, entries.Count);
        Assert.Equal("entry 2", entries[0].Message);
        Assert.Equal("entry 4", entries[^1].Message);
    }

    [Fact]
    public void Raises_event_per_entry_and_clear_empties()
    {
        using var sink = new RingBufferLogSink();
        var logger = sink.CreateLogger("cat");
        int raised = 0;
        sink.EntryAdded += (_, _) => raised++;

        logger.LogInformation("one");
        logger.LogInformation("two");

        Assert.Equal(2, raised);

        sink.Clear();
        Assert.Empty(sink.GetSnapshot());
    }
}
