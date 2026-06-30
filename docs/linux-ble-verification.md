# Linux BLE verification

Manual checklist for validating the experimental BlueZ backend on a real Linux
machine.

## Environment

- Linux desktop with BlueZ running on the system bus.
- Bluetooth adapter powered on.
- Heart-rate tracker exposing the Bluetooth SIG Heart Rate Service (0x180D).
- .NET 10 SDK for source runs, or a `linux-x64` / `linux-arm64` release zip.

Useful host checks:

```sh
bluetoothctl show
bluetoothctl list
busctl --system tree org.bluez
```

`bluetoothctl show` should report a powered adapter. `busctl` should show
`/org/bluez/hci0` or another adapter object.

## Probe scan

Start heart-rate sharing on the tracker first, then run:

```sh
dotnet run --project src/PulseRelay.Probe -f net10.0 -- scan --service 180D --verbose
```

Expected:

- The log says it is scanning with BlueZ.
- At least one advertisement is logged with the tracker address/name.
- The final summary reports at least one Heart Rate Service advertiser.

If `scan --service 180D` finds nothing, compare with:

```sh
dotnet run --project src/PulseRelay.Probe -f net10.0 -- scan --all --verbose
```

Interpretation:

- No devices in `scan --all`: adapter/radio/permission problem.
- Devices in `scan --all` but not in `scan --service 180D`: tracker is not
  advertising the Heart Rate Service, or sharing mode is not active.

## Probe connect

```sh
dotnet run --project src/PulseRelay.Probe -f net10.0 -- connect --name "Charge 6" --verbose
```

Expected state flow:

```text
Scanning -> Connecting -> Subscribing -> Subscribed -> Streaming
```

Expected success evidence:

- Device is selected from a 0x180D advertisement.
- BlueZ connects and resolves GATT services.
- Heart Rate Measurement characteristic 0x2A37 is found.
- If BlueZ requires authentication, PulseRelay logs a pairing retry and then
  attempts notification setup once more.
- First valid notification logs `SUCCESS`.
- BPM values are printed continuously.

## Desktop

```sh
dotnet run --project src/PulseRelay.Desktop -f net10.0
```

Expected:

- Settings can select Bluetooth LE device.
- Start follows the same tracker-side sharing flow as Windows.
- Dashboard shows live BPM and OSC state.

## Failure evidence to capture

When reporting a Linux BLE failure, keep:

- distro and version,
- BlueZ version (`bluetoothctl --version`),
- `bluetoothctl show`,
- whether `scan --all` sees the tracker,
- full `PulseRelay.Probe --verbose` output,
- whether pairing prompts appeared.

Pairing/encryption is not fully marked solved until a real-device log confirms
the flow. The current implementation retries once with `Device1.Pair`. If BlueZ
reports missing-agent or rejected-agent errors, the next implementation step is
to add a tested `AgentManager1` / agent flow.
