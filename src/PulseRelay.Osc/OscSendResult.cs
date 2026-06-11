namespace PulseRelay.Osc;

/// <summary>Outcome of a single OSC send attempt.</summary>
/// <param name="Bpm">The BPM value that was (or failed to be) sent.</param>
/// <param name="Error">Null on success; otherwise a short failure message.</param>
public sealed record OscSendResult(int Bpm, string? Error)
{
    public bool Success => Error is null;
}
