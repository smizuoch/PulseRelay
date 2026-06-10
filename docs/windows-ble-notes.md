# Windows BLE notes

Implementation notes for `PulseRelay.WindowsBle` (WinRT
`Windows.Devices.Bluetooth` / `GenericAttributeProfile` via the
`net10.0-windows10.0.19041.0` TFM).

Reference guidelines this implementation follows:

- Fitbit / Pixel Watch Heart Rate Sharing Compatibility Guidelines
  (<https://support.google.com/product-documentation/answer/16923066?hl=en>)
- Windows Bluetooth GATT Client
  (<https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client>)
- WinRT APIs in desktop apps
  (<https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps>)

## GATT calls and diagnostics

All discovery/subscription steps use the result-object APIs so failures carry data:

| Step | API | Logged |
|---|---|---|
| Service discovery | `GetGattServicesForUuidAsync(0x180D, Uncached)` | `GattCommunicationStatus`, ATT protocol error, count |
| Characteristic discovery | `GetCharacteristicsForUuidAsync(0x2A37, Uncached)` | status, protocol error, count, `CharacteristicProperties` |
| Subscribe | `WriteClientCharacteristicConfigurationDescriptorWithResultAsync(Notify)` | `GattWriteResult.Status`, `ProtocolError` (hex) |
| Notifications | `ValueChanged` | raw payload hex at Debug level, then parsed BPM |

A malformed notification logs a warning (with payload hex) and the subscription stays
alive — one bad packet must not kill the stream.

## Pairing / SMP Security Request

The compatibility guidelines require the Central to respond to the peripheral's SMP
Security Request — ignoring it makes the tracker drop the link. Strategy:

1. Try the subscription without pairing first (the guidelines also require correct
   no-encryption behavior).
2. If a GATT step fails with `AccessDenied`, `Unreachable`, or ATT error
   0x05 (Insufficient Authentication) / 0x08 (Insufficient Authorization) /
   0x0F (Insufficient Encryption), run **custom pairing**:
   `DeviceInformation.Pairing.Custom` with `ConfirmOnly | DisplayPin` accepted and
   `DevicePairingProtectionLevel.Encryption` requested. Every `PairingRequested` kind
   and the final `DevicePairingResultStatus` are logged.
3. **After pairing, all GATT objects acquired before pairing are treated as stale.**
   The `BluetoothLEDevice` is disposed and device → services → characteristics are
   reacquired from scratch before the single subscription retry.

## Resolvable Private Addresses (RPA)

The tracker advertises an RPA that rotates over time. Consequences honored by the code:

- Bluetooth addresses are **session-scoped**: used only to connect right after a scan,
  never written to disk, and always logged with an `(RPA — session-scoped, do not
  persist)` tag.
- Reconnection across sessions is always scan-first. `FromBluetoothAddressAsync`
  returning null for a previously working address is expected RPA behavior, not a bug.

## OPEN ASSUMPTION — Central Device Name (0x2A00)

The compatibility guidelines require the **Central** (the Windows PC) to expose a
readable Device Name characteristic (0x2A00) under the Generic Access service (0x1800)
to the peripheral.

**Status: UNVERIFIED.** We believe the Windows Bluetooth stack exposes the host's GAP
service (including the PC name) to remote peers, and there is no public WinRT API to
provide it from app code — but this has not been proven against a real Charge 6.
This must not be marked solved until a real-device verification log shows the tracker
completing the Share flow.

How to evaluate during verification:

- Note whether the tracker displays the PC's name in its share prompt (evidence the
  peripheral could read 0x2A00).
- If the tracker connects and then silently drops, or the Share handshake never
  completes **while every GATT call from our side logs `Success`**, suspect this
  assumption first. The timestamped `ConnectionStatusChanged` transitions plus pairing
  status in the log are the evidence trail.
- Independent check: from a phone running a BLE scanner app (e.g. nRF Connect), connect
  to the PC and try to read GAP 0x1800 / 0x2A00 — this verifies what the Windows host
  actually exposes, with no tracker involved.
- If Windows turns out not to expose 0x2A00, options to investigate: publishing a GAP
  service via `GattServiceProvider` (may conflict with the system GAP service), or the
  device's Bluetooth adapter/driver settings. Do not guess; collect logs first.

## Warning-suppression log

`TreatWarningsAsErrors=true` is global. Any platform-specific suppression must be
recorded here with the warning ID and rationale.

| Warning | Scope | Rationale |
|---|---|---|
| _(none so far)_ | | |

Note: `Directory.Build.props` sets `EnableWindowsTargeting=true` so the Windows TFM
projects compile (not run) on macOS/Linux for development. This is a build setting,
not a warning suppression.
