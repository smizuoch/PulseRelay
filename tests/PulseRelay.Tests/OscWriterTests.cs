using PulseRelay.Osc;
using Xunit;

namespace PulseRelay.Tests;

public class OscWriterTests
{
    [Fact]
    public void Encodes_short_address_with_padding()
    {
        // "/a" + null => 3 bytes, padded to 4; ",i" + null => 3, padded to 4; int32 BE
        byte[] expected =
        [
            (byte)'/', (byte)'a', 0x00, 0x00,
            (byte)',', (byte)'i', 0x00, 0x00,
            0x00, 0x00, 0x00, 0x48,
        ];

        Assert.Equal(expected, OscWriter.WriteMessage("/a", 72));
    }

    [Theory]
    [InlineData("/abc", 8)]   // 4 chars + null = 5 -> 8
    [InlineData("/ab", 4)]    // 3 chars + null = 4 -> 4 (no extra padding)
    [InlineData("/abcd", 8)]  // 5 chars + null = 6 -> 8
    [InlineData("/abcde", 8)] // 6 chars + null = 7 -> 8
    [InlineData("/abcdef", 8)] // 7 chars + null = 8 -> 8
    [InlineData("/abcdefg", 12)] // 8 chars + null = 9 -> 12
    public void Pads_address_to_four_byte_boundary(string address, int expectedAddressBlock)
    {
        byte[] message = OscWriter.WriteMessage(address, 0);

        // address block + 4-byte type tag block + 4-byte int32
        Assert.Equal(expectedAddressBlock + 4 + 4, message.Length);

        // address bytes, then nulls up to the block boundary
        for (int i = 0; i < address.Length; i++)
        {
            Assert.Equal((byte)address[i], message[i]);
        }

        for (int i = address.Length; i < expectedAddressBlock; i++)
        {
            Assert.Equal(0x00, message[i]);
        }
    }

    [Fact]
    public void Encodes_int32_as_big_endian()
    {
        byte[] message = OscWriter.WriteMessage("/ab", 0x01020304);

        Assert.Equal([0x01, 0x02, 0x03, 0x04], message[^4..]);
    }

    [Fact]
    public void Encodes_negative_int32()
    {
        byte[] message = OscWriter.WriteMessage("/ab", -1);

        Assert.Equal([0xFF, 0xFF, 0xFF, 0xFF], message[^4..]);
    }

    [Fact]
    public void Encodes_default_heart_rate_address()
    {
        string address = HeartRateOscPublisher.DefaultAddress;
        byte[] message = OscWriter.WriteMessage(address, 72);

        // "/avatar/parameters/VRCOSC/Heartrate/Value" = 42 chars + null = 43 -> 44
        Assert.Equal(44 + 4 + 4, message.Length);
        Assert.Equal((byte)'/', message[0]);
        Assert.Equal(0x00, message[43]);
        Assert.Equal((byte)',', message[44]);
        Assert.Equal((byte)'i', message[45]);
        Assert.Equal([0x00, 0x00, 0x00, 0x48], message[^4..]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-leading-slash")]
    [InlineData("/日本語")]
    public void Rejects_invalid_addresses(string address)
    {
        Assert.Throws<ArgumentException>(() => OscWriter.WriteMessage(address, 0));
    }
}
