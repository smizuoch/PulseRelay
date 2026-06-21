using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;

namespace PulseRelay.Osc;

/// <summary>
/// Forwards heart-rate samples from an <see cref="IHeartRateSource"/> to an OSC endpoint
/// as int32 BPM messages. Defaults match VRChat's local OSC receiver.
/// </summary>
public sealed class HeartRateOscPublisher : IDisposable
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 9000;
    public const string DefaultAddress = "/avatar/parameters/VRCOSC/Heartrate/Value";

    private readonly OscUdpSender _sender;
    private readonly string _address;
    private readonly ILogger _logger;
    private IHeartRateSource? _source;

    public HeartRateOscPublisher(
        string host = DefaultHost,
        int port = DefaultPort,
        string address = DefaultAddress,
        ILogger<HeartRateOscPublisher>? logger = null)
    {
        // Validate the address eagerly so a bad CLI value fails at startup, not per sample.
        _ = OscWriter.WriteMessage(address, 0);
        _sender = new OscUdpSender(host, port);
        _address = address;
        _logger = logger ?? NullLogger<HeartRateOscPublisher>.Instance;
    }

    /// <summary>
    /// Raised after every send attempt with its outcome. Fires on the thread that delivered
    /// the sample (typically a BLE or timer callback thread), never the UI thread.
    /// </summary>
    public event EventHandler<OscSendResult>? SendCompleted;

    public void Attach(IHeartRateSource source)
    {
        if (_source is not null)
        {
            throw new InvalidOperationException("Publisher is already attached to a source.");
        }

        _source = source;
        source.SampleReceived += OnSampleReceived;
        _logger.LogInformation(
            "OSC publishing enabled: {Address} -> udp://{Host}:{Port}", _address, _sender.Host, _sender.Port);
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _source.SampleReceived -= OnSampleReceived;
            _source = null;
        }

        _sender.Dispose();
    }

    private void OnSampleReceived(object? sender, HeartRateSample sample)
    {
        OscSendResult result;
        try
        {
            _sender.Send(OscWriter.WriteMessage(_address, sample.Bpm));
            _logger.LogDebug("OSC sent {Address} = {Bpm}", _address, sample.Bpm);
            result = new OscSendResult(sample.Bpm, Error: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send OSC message to {Host}:{Port}", _sender.Host, _sender.Port);
            result = new OscSendResult(sample.Bpm, ex.Message);
        }

        try
        {
            SendCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OSC send observer failed");
        }
    }
}
