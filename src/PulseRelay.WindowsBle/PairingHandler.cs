using Microsoft.Extensions.Logging;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace PulseRelay.WindowsBle;

/// <summary>
/// Custom pairing that accepts the peripheral's SMP Security Request instead of ignoring it.
/// The Fitbit compatibility guidelines require the Central to respond to security requests;
/// rejecting or ignoring them makes compatible trackers drop the connection.
/// </summary>
public static class PairingHandler
{
    public static async Task<DevicePairingResultStatus> PairAsync(
        DeviceInformation deviceInformation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var pairing = deviceInformation.Pairing;
        logger.LogInformation(
            "Pairing state before attempt: IsPaired={IsPaired} CanPair={CanPair}",
            pairing.IsPaired,
            pairing.CanPair);

        if (pairing.IsPaired)
        {
            return DevicePairingResultStatus.AlreadyPaired;
        }

        var custom = pairing.Custom;
        TypedEventHandler<DeviceInformationCustomPairing, DevicePairingRequestedEventArgs> handler = (_, args) =>
        {
            logger.LogInformation("PairingRequested: kind={Kind}", args.PairingKind);
            switch (args.PairingKind)
            {
                case DevicePairingKinds.ConfirmOnly:
                    args.Accept();
                    break;
                case DevicePairingKinds.DisplayPin:
                    logger.LogInformation("Pairing PIN to confirm on the device: {Pin}", args.Pin);
                    args.Accept();
                    break;
                default:
                    logger.LogWarning(
                        "Unsupported pairing kind {Kind}; not accepting. Pairing will likely fail.",
                        args.PairingKind);
                    break;
            }
        };

        custom.PairingRequested += handler;
        try
        {
            var operation = custom.PairAsync(
                DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin,
                DevicePairingProtectionLevel.Encryption);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
            var result = await operation.AsTask(timeoutCts.Token).ConfigureAwait(false);

            logger.LogInformation(
                "Pairing result: {Status} (protectionLevelUsed={Protection})",
                result.Status,
                result.ProtectionLevelUsed);
            return result.Status;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Bluetooth pairing did not complete within 30 seconds.");
        }
        finally
        {
            custom.PairingRequested -= handler;
        }
    }
}
