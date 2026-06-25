using PulseRelay.Osc;
using Xunit;

namespace PulseRelay.Tests;

public class HeartRateOscPublisherTests
{
    [Fact]
    public void Constructor_rejects_invalid_osc_address()
    {
        Assert.Throws<ArgumentException>(() => new HeartRateOscPublisher(address: "heart/rate"));
    }

    [Fact]
    public void Attach_rejects_second_source()
    {
        using var publisher = new HeartRateOscPublisher();
        var first = new FakeHeartRateSource();
        var second = new FakeHeartRateSource();

        publisher.Attach(first);

        Assert.Throws<InvalidOperationException>(() => publisher.Attach(second));
    }

    [Fact]
    public void Emits_send_completed_for_samples()
    {
        using var publisher = new HeartRateOscPublisher();
        var source = new FakeHeartRateSource();
        OscSendResult? result = null;
        publisher.SendCompleted += (_, sendResult) => result = sendResult;
        publisher.Attach(source);

        source.EmitSample(72, DateTimeOffset.UtcNow);

        Assert.NotNull(result);
        Assert.Equal(72, result.Bpm);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Observer_exception_does_not_escape_sample_delivery()
    {
        using var publisher = new HeartRateOscPublisher();
        var source = new FakeHeartRateSource();
        publisher.SendCompleted += (_, _) => throw new InvalidOperationException("observer failed");
        publisher.Attach(source);

        source.EmitSample(72, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Dispose_detaches_from_source()
    {
        var publisher = new HeartRateOscPublisher();
        var source = new FakeHeartRateSource();
        int sends = 0;
        publisher.SendCompleted += (_, _) => sends++;
        publisher.Attach(source);
        publisher.Dispose();

        source.EmitSample(72, DateTimeOffset.UtcNow);

        Assert.Equal(0, sends);
    }
}
