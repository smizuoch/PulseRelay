# Desktop verification notes (sanitized)

No raw logs, BLE addresses, or screenshots in this file — addresses are session-scoped
RPAs and must never be recorded as identities.

## Windows 11 + Charge 6 (2026-06)

Run command: `dotnet run --project src/PulseRelay.Desktop -f net10.0-windows10.0.19041.0`

- Desktop UI rendered correctly; dashboard showed live BPM.
- Streamed continuously from a Charge 6 for ~30 minutes.
- OSC output was received by the target application (VRChat used as the test receiver).
- **One unexplained mid-session disconnect occurred** and required a manual reconnect;
  after reconnecting, the session stayed stable. Cause not identified (suspects: Share
  screen timeout on the tracker, or a transient link drop). This motivated the
  supervisor-based auto-reconnect that now ships.

## Auto-reconnect behavior (current)

While the user has clicked Start (intent: running), the `BridgeSupervisor` keeps the
bridge alive:

- Any drop — initial connect failure, mid-session disconnect, or >30 s of sample silence
  while streaming — schedules a fresh connect attempt through the source factory
  (new source object every time; nothing address-shaped is reused or persisted).
- Backoff: 1 s, 3 s, 10 s, then every 30 s indefinitely. The sequence resets once an
  attempt reaches Streaming, or on manual Reconnect.
- Stop cancels any pending retry and stops the source cleanly; nothing auto-retries after
  an explicit Stop.
- If a BLE run never establishes a connection, it stops after 30 minutes. The desktop app
  remains open and can be started again. Establishing a connection permanently disables
  this limit for that run; streaming and later reconnects remain unlimited.
- A connection that subscribes but produces no first sample for 60 seconds is recycled.
- Staleness: the UI shows "No data for Ns" after 10 s of silence (display only) and the
  watchdog forces a reconnect after 30 s. Neither applies before the first sample — the
  Charge 6 has been observed to deliver its first notification ~19 s after subscription.
- "Bluetooth unavailable" failure copy exists, but mapping it to a distinct exception is
  best-effort: with the radio off, the Windows stack typically surfaces a scan timeout
  (mapped to "device not found"). Revisit if a distinct exception type is observed.

Reconnect logic is covered by deterministic unit tests (`BridgeSupervisorTests`, fake
sources + `FakeTimeProvider`); the manual checklist below covers the real-hardware paths.

## Manual Windows checklist (run after reliability/localization changes)

1. Start → Charge 6 sharing → Streaming with live BPM; OSC values visible in receiver.
2. Exit the Share screen mid-stream → "Device disconnected — trying again…" appears and
   the bridge recovers by itself once sharing is restarted.
3. Click Stop during a retry wait → retries cease (observe ~60 s); status "Not connected".
4. Start with sharing off → retry cycle at 1 s/3 s/10 s/30 s; enable sharing → connects.
5. Click Reconnect during a 30 s wait → immediate attempt.
6. Language: System → 日本語 switches the dashboard live; restart keeps the choice;
   System follows the OS language.
7. Logs (app + probe) remain English/ASCII; `settings.json` contains no address/MAC field.
8. CLI probe `connect` still streams (regression sanity; probe code untouched).

## Manual Windows checklist (2026-06 polish pass: display name, OSC default, localization, UX)

1. Delete `%APPDATA%\PulseRelay\settings.json`, launch → Output card shows **OSC on**
   before the first Start (fresh settings default).
2. Turn OSC off, restart the app → stays off (explicit choice persisted). Turn it back on.
3. Connect to the Charge 6 → device line shows **"BLE Charge 6"** (or the name filter /
   "Bluetooth LE device" fallback) — never "BLE \<unknown\>" while connected.
4. Switch 日本語 ↔ English repeatedly → every visible string follows, specifically:
   Start/開始, Turn on/オンにする, Turn off/オフにする, and the connect hint
   "Start heart-rate sharing on your device first." / 「先にデバイス側で心拍共有を開始してください。」
5. The dashboard no longer shows the "How do I share my heart rate?" guide expander;
   only the one-line hint appears while stopped.
6. Charge 6 still streams BPM; OSC values still arrive; Stop still prevents reconnect;
   only one active source exists at a time (Start is idempotent, tray/dashboard agree).
7. Settings dialog: invalid port (`0`, `70000`, `abc`) and address without `/` are
   rejected with a localized error; valid edits persist and re-apply OSC live.
8. Diagnostics window: shows state/source/BPM/OSC counters and recent logs (English);
   "Copy diagnostics" output contains no `AA:BB:CC:DD:EE:FF`-style addresses.
9. Tray icon: Show/Start/Stop/OSC toggle/Quit work in both languages; Quit exits cleanly
   with no ghost tray icon and no lingering process.
10. Close the main window with "hide to tray" enabled → bridge keeps running and Show
    restores the same window. Disable it → close performs cleanup and exits.
11. Start with sharing disabled and observe 30 minutes → bridge stops but the app and tray
    remain available. A run that has connected/streamed is not stopped by this limit.
