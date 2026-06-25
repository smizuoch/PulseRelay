using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.App.Settings;
using PulseRelay.Core.Sources;
using Xunit;

namespace PulseRelay.Tests;

public class MockOnlySourceFactoryTests
{
    [Fact]
    public void Reports_ble_as_unsupported()
    {
        var factory = new MockOnlySourceFactory(NullLoggerFactory.Instance);

        Assert.False(factory.SupportsBle);
    }

    [Fact]
    public async Task Creates_mock_source_for_mock_settings()
    {
        var factory = new MockOnlySourceFactory(NullLoggerFactory.Instance);

        await using var source = factory.Create(new AppSettings { SourceKind = HeartRateSourceKind.Mock });

        Assert.IsType<MockHeartRateSource>(source);
    }

    [Fact]
    public void Rejects_unknown_source_kind()
    {
        var factory = new MockOnlySourceFactory(NullLoggerFactory.Instance);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            factory.Create(new AppSettings { SourceKind = (HeartRateSourceKind)999 }));
    }
}
