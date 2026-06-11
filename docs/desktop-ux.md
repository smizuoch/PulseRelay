# Desktop UX design

PulseRelay's desktop app is a local heart-rate bridge: BLE heart-rate devices in, OSC out.
The UI stays generic — Fitbit Charge 6 and VRChat appear only as examples in device guides
and default values, never in product chrome or public type names.

## UX goals

1. Five minutes from download to BPM in the OSC app for a non-technical user.
2. Honesty about the manual step: the app never claims it can start heart-rate sharing on
   the device; it teaches the ritual and waits visibly.
3. One glance answers: What's my BPM? Device connected? OSC sending? Data fresh?
4. Calm utility aesthetic: dark-first, neutral surfaces, one soft pulse accent (`#E8637E`).
   No EKG-monitor styling, no neon, no medical iconography.
5. Power features (GATT logs, raw packets, RSSI) live in Diagnostics, not the main screen.
6. The CLI probe remains the diagnostics ground truth.

## Layers

```
PulseRelay.Desktop (Avalonia, dual TFM)      UI only; BLE touched solely in
  └── PulseRelay.App (net10.0)               WindowsBleSourceFactory (#if WINDOWS_BLE)
        ├── BridgeSession                    owns IHeartRateSource + HeartRateOscPublisher,
        │                                    condenses events into immutable BridgeSnapshot
        ├── SettingsStore / AppSettings      JSON at <ApplicationData>/PulseRelay/settings.json
        ├── RingBufferLogSink                ILoggerProvider feeding the Diagnostics view
        └── BridgeStatusCopy                 shared user-facing wording
```

`SnapshotChanged` fires on source/timer threads; ViewModels marshal via
`Dispatcher.UIThread.Post`. The UI never touches BLE types directly.

## State model

| `HeartRateSourceState` | `BridgeStatus` | Main-screen copy |
|---|---|---|
| Idle | NotConnected | "Not connected" |
| Scanning | Searching | "Looking for your device…" |
| Connecting / Subscribing | Connecting | "Connecting…" |
| Subscribed | WaitingForData | "Connected — waiting for the first reading…" |
| Streaming | Streaming | "Receiving heart rate" |
| Streaming, no sample > 10 s | Stale (derived) | "No data for Ns — is the device still sharing?" |
| Disconnected | Disconnected | "Device disconnected" |
| Failed | Failed | error copy below |

Staleness is evaluated by a 1-second UI ticker via `BridgeSnapshot.EffectiveStatus`; it
never mutates source state. OSC has its own status (`Off` / `On` / `Error`) driven by
`HeartRateOscPublisher.SendCompleted`.

## Error copy rules

No GATT/CCCD/RPA jargon outside Diagnostics; never blame the user; always say what to do
next; never promise the app can fix the device side. Key cases:

- Scan timeout → "Check that your device is actively sharing its heart rate and is near
  this computer. Sharing screens often time out — restart sharing, then retry."
- Mid-session disconnect → "The sharing screen may have closed or timed out on the device.
  Start sharing again, then reconnect."
- OSC send failure → Output card shows "Sending failed — check the host and port."
  Non-blocking; the heart-rate stream continues.
- Malformed packets are Diagnostics-only; the stream stays alive.

## Identity & RPA rules

- Settings deliberately have **no BLE address field** — peripheral addresses are RPAs and
  must never persist as stable identities. Only the user-typed device name filter persists
  (guarded by a unit test on the serialized JSON).
- The Charge 6 connects to one app/equipment at a time; the user must open the
  **HR on equipment** tile, tap **Share**, then **Start**, and keep that screen active.
  This appears as one entry in a generic device-guide list.

## Views

- **Dashboard** (built): BPM hero with gentle 1.2 s pulse while streaming, status line,
  freshness line, Device card (state dot, description, skin contact, Connect/Disconnect),
  Output card (OSC state dot, target, address, on/off toggle).
- **First-run wizard** (planned): Welcome → Prepare your device (expandable device guides,
  explicit "PulseRelay can't do this step for you") → Connect (live status, timeout help) →
  Output (OSC defaults + test send) → Done. Skippable at every step.
- **Diagnostics** (planned): session summary (state, samples, last raw hex, parsed BPM, RR,
  OSC counters) + ring-buffer log with level filter / pause / copy, and a copyable CLI probe
  command for deeper output.
- **Settings** (planned): source kind, device name filter, scan timeout, OSC host/port/
  address + test send, theme, auto-connect on launch, hide-to-tray, re-run wizard.
- **Tray icon** (planned): status-tinted heart, BPM tooltip, Show/Connect/Disconnect/
  OSC toggle/Quit menu; window close hides to tray (configurable).

## Status colors

gray `#6B7077` idle/off · light gray `#A8AEB8` working · accent `#E8637E` good ·
amber `#C9A227` stale/OSC error · muted red `#B3565E` failed/disconnected.

## Verification

- macOS: `dotnet build PulseRelay.sln && dotnet test`; run
  `dotnet run --project src/PulseRelay.Desktop --framework net10.0` (mock source is
  auto-selected when BLE is unavailable).
- Windows: run the Windows TFM build against a real Charge 6: wizard/connect flow, scan
  timeout, mid-session disconnect (exit the Share screen), OSC into the receiving app,
  30-minute soak.
