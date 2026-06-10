# PulseRelay

PulseRelay is a local bridge between Bluetooth LE heart-rate sources and OSC-compatible
applications. It connects directly to any tracker that implements the standard Bluetooth
**Heart Rate Service (0x180D)** — no phone, no cloud, no vendor API in the real-time path —
and can forward BPM to a local OSC endpoint over UDP.

Current status: **CLI probe stage.** The first milestone is proving that a compatible
tracker can stream Heart Rate Measurement (0x2A37) notifications to a Windows 11 PC.
No GUI yet, by design.

## Layout

| Project | Target | Purpose |
|---|---|---|
| `src/PulseRelay.Core` | `net10.0` | Platform-neutral models, 0x2A37 parser, source interfaces, mock source |
| `src/PulseRelay.Osc` | `net10.0` | Minimal OSC 1.0 encoder + UDP sender |
| `src/PulseRelay.WindowsBle` | `net10.0-windows10.0.19041.0` | WinRT GATT client (scan, pair, subscribe) |
| `src/PulseRelay.Probe` | both | CLI: `scan` / `connect` / `mock` |
| `tests/PulseRelay.Tests` | `net10.0` | xunit unit tests |

See [docs/architecture.md](docs/architecture.md) for the design.

## Building

Any platform (macOS/Linux/Windows), .NET 10 SDK required (pinned via `global.json`):

```sh
dotnet build PulseRelay.CrossPlatform.slnf   # cross-platform projects + tests
dotnet test  PulseRelay.CrossPlatform.slnf
dotnet build PulseRelay.sln                  # full solution incl. Windows BLE (compiles anywhere, runs BLE only on Windows)
```

## Running

On any platform — synthetic heart rate, optionally forwarded over OSC:

```sh
dotnet run --project src/PulseRelay.Probe -f net10.0 -- mock --osc
```

On Windows 11 — real BLE (see [docs/charge6-verification.md](docs/charge6-verification.md)
for the full checklist):

```sh
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- scan --all --verbose
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- scan --service 180D --verbose
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- connect --name "Charge 6" --verbose
```

## Using a Fitbit Charge 6 (or similar tracker)

The connection cannot be fully automated — the tracker requires user interaction:

1. Open the **HR on equipment** tile on the tracker and keep its screen awake.
2. Run `connect`. When the tracker asks to share heart rate, tap **Share**, then **Start**.
3. The tracker connects to **one** equipment/app at a time; disconnect other consumers first.

PulseRelay deliberately does not use the Fitbit Web API, Google Health API, or any cloud
service for real-time BPM.

## OSC output

Default endpoint `127.0.0.1:9000`, default address
`/avatar/parameters/VRCOSC/Heartrate/Value` (int). All configurable via CLI flags.
See [docs/vrchat-osc.md](docs/vrchat-osc.md).

## License

MIT — see [LICENSE](LICENSE).
