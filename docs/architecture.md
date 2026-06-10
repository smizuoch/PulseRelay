# Architecture

## Goals and constraints

- Core logic is platform-neutral; all Windows BLE code is isolated in `PulseRelay.WindowsBle`.
- Heart-rate sources are interchangeable behind `IHeartRateSource` (BLE on Windows, mock everywhere).
- OSC sending is independent of BLE — `PulseRelay.Osc` has no BLE dependency.
- Public names are generic: nothing is named after a specific tracker vendor or consumer app.
- No GUI, installers, auto-updaters, or cloud sync until the BLE probe milestone is proven.

## Project graph

```
PulseRelay.Probe (net10.0; net10.0-windows10.0.19041.0)
 ├── PulseRelay.Core (net10.0)
 ├── PulseRelay.Osc  (net10.0)  ──► PulseRelay.Core
 └── PulseRelay.WindowsBle (net10.0-windows10.0.19041.0, Windows TFM only) ──► PulseRelay.Core

PulseRelay.Tests (net10.0) ──► Core, Osc
```

The probe multi-targets. The plain `net10.0` build has no BLE reference and offers only the
`mock` command; the Windows TFM defines `WINDOWS_BLE` and adds `scan`/`connect`.

`Directory.Build.props` sets `EnableWindowsTargeting=true` so the Windows TFM projects
*compile* on macOS/Linux (reference assemblies only). They can only *run* on Windows.
`PulseRelay.CrossPlatform.slnf` is the macOS-authoritative build/test entry point.

## Data flow

```
BLE tracker ──0x2A37 notification──► BleHeartRateSource ─┐
                                                          ├─ SampleReceived(HeartRateSample)
MockHeartRateSource (timer, sine wave) ──────────────────┘        │
                                                                  ├──► console log (Probe)
                                                                  └──► HeartRateOscPublisher ──UDP──► OSC app
```

## Core contracts

- `HeartRateSample` — parsed 0x2A37 payload: BPM, sensor contact, energy expended (kJ),
  RR intervals (ms), local receive timestamp.
- `HeartRateMeasurementParser.Parse(ReadOnlySpan<byte>, DateTimeOffset)` — pure static,
  no BLE types, fully unit-tested. Throws `FormatException` (with payload hex) on
  malformed input; callers log and keep the stream alive.
- `IHeartRateSource` — `StartAsync` completes at state `Subscribed` (ready, **no data
  yet**). The source reaches `Streaming` only when the first valid sample is parsed.
  Success must never be claimed on a CCCD write alone.

State machine: `Idle → Scanning → Connecting → Subscribing → Subscribed → Streaming`,
with `Disconnected` / `Failed` as terminal-ish states.

## Windows BLE flow (PulseRelay.WindowsBle)

1. `BleAdvertisementScanner` — `BluetoothLEAdvertisementWatcher`, active scanning,
   either filtered on 0x180D or unfiltered (diagnostics). Logs every advertisement
   and watcher state change.
2. `BluetoothLEDevice.FromBluetoothAddressAsync` — addresses are RPAs, treated as
   session-scoped, never persisted.
3. Best-effort read of the peripheral's Device Name (0x2A00) for the log.
4. Service/characteristic discovery and CCCD subscribe via the `*WithResult` APIs so
   `GattCommunicationStatus` + ATT protocol-error bytes are always logged.
5. On auth-related failure (AccessDenied / ATT 0x05/0x08/0x0F): custom pairing via
   `PairingHandler` (accepts the SMP Security Request, requests encryption), then the
   device and all GATT handles are **reacquired from scratch** before one retry.
6. First parsed notification ⇒ `Streaming`.

Details, caveats, and open assumptions: [windows-ble-notes.md](windows-ble-notes.md).

## OSC (PulseRelay.Osc)

Hand-rolled minimal OSC 1.0 encoder (single int32-argument messages only), byte-exact
unit tests. `HeartRateOscPublisher` maps `SampleReceived` → one UDP datagram per sample.
Conventions: [vrchat-osc.md](vrchat-osc.md).
