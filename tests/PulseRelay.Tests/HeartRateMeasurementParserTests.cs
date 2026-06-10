using PulseRelay.Core.HeartRate;
using Xunit;

namespace PulseRelay.Tests;

public class HeartRateMeasurementParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_8bit_heart_rate()
    {
        var sample = HeartRateMeasurementParser.Parse([0x00, 72], Now);

        Assert.Equal(72, sample.Bpm);
        Assert.Equal(SensorContactStatus.NotSupported, sample.SensorContact);
        Assert.Null(sample.EnergyExpendedKilojoules);
        Assert.Empty(sample.RrIntervalsMs);
        Assert.Equal(Now, sample.Timestamp);
    }

    [Fact]
    public void Parses_16bit_heart_rate_above_255()
    {
        // flags bit 0 set => UINT16 little-endian: 0x0140 = 320
        var sample = HeartRateMeasurementParser.Parse([0x01, 0x40, 0x01], Now);

        Assert.Equal(320, sample.Bpm);
    }

    [Theory]
    [InlineData(0b0000_0000, SensorContactStatus.NotSupported)]
    [InlineData(0b0000_0010, SensorContactStatus.NotSupported)] // detected bit without supported bit
    [InlineData(0b0000_0100, SensorContactStatus.NoContact)]
    [InlineData(0b0000_0110, SensorContactStatus.Contact)]
    public void Parses_sensor_contact_states(byte flags, SensorContactStatus expected)
    {
        var sample = HeartRateMeasurementParser.Parse([flags, 70], Now);

        Assert.Equal(expected, sample.SensorContact);
    }

    [Fact]
    public void Parses_energy_expended()
    {
        // flags bit 3 set; energy = 0x2710 = 10000 kJ
        var sample = HeartRateMeasurementParser.Parse([0b0000_1000, 75, 0x10, 0x27], Now);

        Assert.Equal(75, sample.Bpm);
        Assert.Equal(10_000, sample.EnergyExpendedKilojoules);
    }

    [Fact]
    public void Parses_single_rr_interval()
    {
        // flags bit 4 set; RR raw 0x0400 = 1024 => exactly 1000 ms
        var sample = HeartRateMeasurementParser.Parse([0b0001_0000, 70, 0x00, 0x04], Now);

        Assert.Equal([1000.0], sample.RrIntervalsMs);
    }

    [Fact]
    public void Parses_multiple_rr_intervals()
    {
        // RR raw 512 => 500 ms, raw 1024 => 1000 ms
        var sample = HeartRateMeasurementParser.Parse([0b0001_0000, 70, 0x00, 0x02, 0x00, 0x04], Now);

        Assert.Equal([500.0, 1000.0], sample.RrIntervalsMs);
    }

    [Fact]
    public void Parses_combined_flags_payload()
    {
        // 16-bit HR (260), contact supported+detected, energy expended (77), one RR (1024 => 1000 ms)
        byte flags = 0b0001_1111;
        var sample = HeartRateMeasurementParser.Parse(
            [flags, 0x04, 0x01, 0x4D, 0x00, 0x00, 0x04], Now);

        Assert.Equal(260, sample.Bpm);
        Assert.Equal(SensorContactStatus.Contact, sample.SensorContact);
        Assert.Equal(77, sample.EnergyExpendedKilojoules);
        Assert.Equal([1000.0], sample.RrIntervalsMs);
    }

    [Fact]
    public void Empty_payload_throws()
    {
        Assert.Throws<FormatException>(() => HeartRateMeasurementParser.Parse([], Now));
    }

    [Fact]
    public void Truncated_16bit_heart_rate_throws_with_hex_dump()
    {
        var ex = Assert.Throws<FormatException>(() => HeartRateMeasurementParser.Parse([0x01, 0x50], Now));

        Assert.Contains("0150", ex.Message);
    }

    [Fact]
    public void Missing_8bit_heart_rate_throws()
    {
        Assert.Throws<FormatException>(() => HeartRateMeasurementParser.Parse([0x00], Now));
    }

    [Fact]
    public void Truncated_energy_expended_throws()
    {
        Assert.Throws<FormatException>(() => HeartRateMeasurementParser.Parse([0b0000_1000, 70, 0x10], Now));
    }

    [Fact]
    public void Truncated_rr_interval_throws()
    {
        Assert.Throws<FormatException>(() => HeartRateMeasurementParser.Parse([0b0001_0000, 70, 0x00], Now));
    }

    [Fact]
    public void Rr_flag_without_rr_data_throws()
    {
        Assert.Throws<FormatException>(() => HeartRateMeasurementParser.Parse([0b0001_0000, 70], Now));
    }
}
