using System.Net.Sockets;

namespace PulseRelay.Osc;

/// <summary>Sends pre-encoded OSC datagrams over UDP to a fixed endpoint.</summary>
public sealed class OscUdpSender : IDisposable
{
    private readonly UdpClient _client;

    public OscUdpSender(string host, int port)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);

        Host = host;
        Port = port;
        var client = new UdpClient();
        try
        {
            client.Connect(host, port);
            _client = client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public string Host { get; }

    public int Port { get; }

    public void Send(ReadOnlySpan<byte> datagram) => _client.Client.Send(datagram);

    public void Dispose() => _client.Dispose();
}
