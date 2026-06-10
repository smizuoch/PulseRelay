# Charge 6 verification checklist (Windows 11)

Goal of the milestone: **at least one valid Heart Rate Measurement (0x2A37) notification
received from a real Fitbit Charge 6 and parsed into BPM** by `PulseRelay.Probe` on
Windows 11 x64. Nothing short of that counts as success.

User-flow reference: <https://support.google.com/googlehealth/answer/14236705?hl=en>

## Prerequisites

- [ ] Windows 11 x64 PC with a BLE-capable adapter; Bluetooth turned on.
- [ ] Settings → Privacy & security → Bluetooth: access allowed for desktop apps.
- [ ] .NET 10 SDK installed (`dotnet --info` shows a 10.0.x SDK).
- [ ] Repo builds clean: `dotnet build PulseRelay.sln` → 0 warnings, 0 errors.
- [ ] Charge 6 charged, worn on the wrist (sensor contact), **disconnected from any
      other HR consumer** — it accepts one equipment/app connection at a time. If the
      Fitbit phone app or gym equipment is currently consuming HR, disconnect it.

## Step 1 — radio triage (before involving the tracker)

```powershell
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- scan --all --verbose
```

- [ ] Watcher starts without a `Stopped` error and *some* nearby BLE devices appear.
  - Zero advertisements from every device ⇒ Windows-side problem (radio, drivers,
    privacy settings) — fix before blaming the tracker.

## Step 2 — tracker advertising triage

On the Charge 6: open the **HR on equipment** tile and keep the screen awake. Then:

```powershell
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- scan --service 180D --verbose
```

- [ ] A device advertising 0x180D appears (name may or may not be present).
  - Absent here but visible in `scan --all` ⇒ advertising without visible 0x180D.
  - Absent in both ⇒ not advertising: HR-on-equipment not open, screen asleep, or
    already connected to another consumer.
  - Very low RSSI (< -85 dBm) ⇒ move closer; range/signal issue.
- [ ] Save both scan logs.

## Step 3 — connect and stream

```powershell
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- connect --name "Charge 6" --verbose
```

(If the tracker advertises without a name, rerun without `--name`.)

Expected sequence:

1. Probe logs the selected device and connects.
2. Possibly: Windows pairing prompt and/or GATT auth failure followed by an automatic
   pairing attempt — **watch the tracker** for its share prompt.
3. On the tracker: tap **Share**, then **Start**. (This is mandatory user interaction;
   PulseRelay does not and cannot automate it.)
4. Probe logs `Subscribed, waiting for first Heart Rate Measurement notification...`
5. **SUCCESS criterion:** a Debug line with the raw 0x2A37 payload hex **and** an
   Info line `SUCCESS: first valid Heart Rate Measurement parsed...` followed by
   `BPM=...` lines.

- [ ] Capture the complete console output and store it (e.g. `logs/` is gitignored).

## Step 4 — Central Device Name (0x2A00) observation

This run is also the test of the open assumption documented in
[windows-ble-notes.md](windows-ble-notes.md#open-assumption--central-device-name-0x2a00):

- [ ] Did the Charge 6 show the PC's name in its share prompt? (record yes/no)
- [ ] Did the Share flow complete? (record yes/no)
- Only a completed Share flow with streaming BPM allows marking the 0x2A00 assumption
  verified. If the flow failed while our GATT logs look healthy, follow the
  troubleshooting steps in windows-ble-notes.md.

## Step 5 — optional OSC end-to-end

With VRChat (or any OSC monitor on UDP 9000) running:

```powershell
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- connect --name "Charge 6" --osc
```

- [ ] `/avatar/parameters/VRCOSC/Heartrate/Value` updates with live BPM.

## Step 6 — negative checks (record behavior + logs)

- [ ] Decline the Share prompt on the tracker → probe should log the pairing/GATT
      failure clearly, not hang silently.
- [ ] Leave the HR-on-equipment screen mid-stream → expect disconnect, logged with
      timestamped `ConnectionStatusChanged`.
- [ ] Walk out of range mid-stream → same expectation.
- [ ] Re-run `connect` afterwards → must work via a fresh scan (RPA may have rotated;
      that must not matter).

## Reporting results

Paste the captured logs into an issue/notes including: Windows build, adapter model,
tracker firmware version (Fitbit app → device details), and the checklist outcomes.
Failure logs are as valuable as success logs — every GATT status, protocol error byte,
pairing status, and watcher state change is in the output specifically so failures can
be diagnosed remotely.
