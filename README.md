# PulseRelay

日本語版は [README.ja.md](README.ja.md) をご覧ください。

PulseRelay is a local bridge between Bluetooth LE heart-rate devices and OSC-compatible
applications. It connects directly to any tracker that implements the standard Bluetooth
**Heart Rate Service (0x180D)** — no phone, no cloud, no vendor API in the real-time path —
and forwards BPM to a local OSC endpoint over UDP (for example VRChat).

Current status: **desktop app + CLI probe.** The Avalonia desktop app streams live BPM
from a real tracker on Windows 11 and forwards it over OSC; this has been verified on
hardware with a Fitbit Charge 6. macOS/Linux builds run with a simulated source
(Bluetooth LE support is Windows-only for now).

## Quick start (Windows 11)

```sh
dotnet run --project src/PulseRelay.Desktop -f net10.0-windows10.0.19041.0
```

1. Start heart-rate sharing **on your device first** (see the Charge 6 steps below).
2. Click **Start** in PulseRelay. The dashboard shows the device, live BPM, and OSC state.
3. OSC output is **on by default** and sends to `127.0.0.1:9000` at
   `/avatar/parameters/VRCOSC/Heartrate/Value`.

## Using a Fitbit Charge 6 (example device)

The tracker side cannot be automated — PulseRelay can never press Share for you.
Each session:

1. On the tracker, open the **HR on equipment** tile and keep its screen awake.
2. Tap **Share**, then **Start** on the tracker when it asks to share heart rate.
3. Click **Start** in PulseRelay.
4. Keep sharing active on the tracker for the whole session — leaving the Share screen
   stops the broadcast (PulseRelay will auto-reconnect once you start sharing again).

Note: the Charge 6 connects to **one** app or piece of equipment at a time. Close other
connections (other PCs, gym equipment, the CLI probe) before starting.

Any other tracker that exposes the standard Heart Rate Service should work the same way;
use its own "broadcast/share heart rate" feature and set the device name filter in
PulseRelay's settings if several BLE devices are nearby.

## OSC output

| Setting | Default |
|---|---|
| Host | `127.0.0.1` |
| Port | `9000` |
| Address | `/avatar/parameters/VRCOSC/Heartrate/Value` (int) |

All configurable in the app's settings (and via CLI flags for the probe).
See [docs/vrchat-osc.md](docs/vrchat-osc.md).

## Troubleshooting

- **The app can't find the device** — make sure the device is actually sharing
  (Charge 6: the "HR on equipment" screen must be open with Share started), Bluetooth is
  on, and the device is near the PC. If several BLE devices are around, set the device
  name filter (e.g. `Charge 6`) in settings.
- **The device line shows a generic name** — the UI shows the device's advertised name
  once connected (e.g. `BLE Charge 6`), falling back to your device name filter or
  "Bluetooth LE device". It should never show `BLE <unknown>` while connected; if it
  does, please report it.
- **OSC isn't received** — check the receiving app listens on the configured host/port
  (VRChat: enable OSC in the action menu; default port 9000) and that the OSC address
  matches what your avatar/receiver expects. The Output card shows send errors.
- **"Device disconnected" right after connecting** — the device is probably still
  connected to another app or equipment. Disconnect it everywhere else, restart sharing,
  and Start again.
- **No data after ~10 s** — the dashboard flags stale data and reconnects automatically
  after 30 s of silence. First readings can take ~20 s on some trackers; that is normal.
  If no first measurement arrives for 60 seconds after connecting, PulseRelay creates a
  fresh connection.
- **Started without enabling sharing** — if no BLE connection is established for 30
  continuous minutes, PulseRelay stops the bridge to avoid scanning indefinitely. The app
  and tray stay open so you can enable sharing and press Start again. Once a connection has
  been established, this 30-minute limit is disabled; active streaming has no time limit.
- **Closing the main window** — by default the window hides to the tray and the bridge
  keeps running. This can be disabled in Settings. Use Quit in the tray menu for an orderly
  shutdown.

## Layout

| Project | Target | Purpose |
|---|---|---|
| `src/PulseRelay.Core` | `net10.0` | Platform-neutral models, 0x2A37 parser, source interfaces, mock source |
| `src/PulseRelay.Osc` | `net10.0` | Minimal OSC 1.0 encoder + UDP sender |
| `src/PulseRelay.WindowsBle` | `net10.0-windows10.0.19041.0` | WinRT GATT client (scan, pair, subscribe) |
| `src/PulseRelay.App` | `net10.0` | Bridge session/supervisor, settings, localization |
| `src/PulseRelay.Desktop` | both | Avalonia desktop app (dashboard) |
| `src/PulseRelay.Probe` | both | CLI: `scan` / `connect` / `mock` |
| `tests/PulseRelay.Tests` | `net10.0` | xunit unit + headless UI tests |

See [docs/architecture.md](docs/architecture.md) for the design.

## Building

Any platform (macOS/Linux/Windows), .NET 10 SDK required (pinned via `global.json`):

```sh
dotnet build PulseRelay.CrossPlatform.slnf   # cross-platform projects + tests
dotnet test  PulseRelay.CrossPlatform.slnf
dotnet build PulseRelay.sln                  # full solution incl. Windows BLE (compiles anywhere, runs BLE only on Windows)
```

## Running the CLI probe

On any platform — synthetic heart rate, optionally forwarded over OSC:

```sh
dotnet run --project src/PulseRelay.Probe -f net10.0 -- mock --osc
```

On Windows 11 — real BLE (see [docs/charge6-verification.md](docs/charge6-verification.md)
for the full checklist):

```sh
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- scan --service 180D --verbose
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- connect --name "Charge 6" --verbose
```

PulseRelay deliberately does not use the Fitbit Web API, Google Health API, or any cloud
service for real-time BPM, and never persists Bluetooth addresses (tracker addresses are
rotating RPAs, not stable identities).

## License

MIT — see [LICENSE](LICENSE).
