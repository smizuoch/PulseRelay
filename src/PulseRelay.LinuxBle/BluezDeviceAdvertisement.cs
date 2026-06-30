namespace PulseRelay.LinuxBle;

public sealed record BluezDeviceAdvertisement(
    string ObjectPath,
    string Address,
    string AddressType,
    string Name,
    IReadOnlyList<string> ServiceUuids,
    short RssiDbm);
