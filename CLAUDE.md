# PulseRelay Claude Instructions

PulseRelay is a local heart-rate bridge. The first critical milestone is proving direct Windows BLE connectivity with Fitbit Charge 6 using the standard BLE Heart Rate Service.

## Priorities

1. Charge 6 BLE proof-of-connectivity comes before GUI, packaging, or polish.
2. Keep the project generic. Do not make public names Fitbit-, Charge6-, or VRChat-specific.
3. Use C# and .NET 10.
4. macOS is the primary development environment, but Windows 11 x64 is the BLE runtime target.
5. Do not use Fitbit Web API, Google Health API, Pulsoid, or cloud services for real-time BPM.
6. Do not pretend the Charge 6 can be fully automated. The user must open HR on equipment and tap Share.

## Architecture

- Core logic must be platform-neutral.
- Windows BLE code must be isolated in PulseRelay.WindowsBle.
- Use interfaces for heart-rate sources.
- Use mock sources for macOS development.
- Keep OSC sending independent from BLE.

## Charge 6 Constraints

Follow:
- https://support.google.com/product-documentation/answer/16923066?hl=en
- https://support.google.com/googlehealth/answer/14236705?hl=en

Important:
- Heart Rate Service UUID: 0x180D
- Heart Rate Measurement UUID: 0x2A37
- Generic Access Service UUID: 0x1800
- Device Name Characteristic UUID: 0x2A00
- Do not ignore SMP Security Request.
- Be careful with RPA. Do not persist temporary MAC addresses as stable identities.
- Charge 6 connects to one equipment/app at a time.

## Verification

Before claiming success, run relevant build/test commands and cite the actual command output.

On macOS:
- dotnet build cross-platform projects
- dotnet test

On Windows:
- dotnet build
- run PulseRelay.Probe against Charge 6
- success requires at least one valid Heart Rate Measurement notification and parsed BPM.

## Scope control

Do not add GUI, installers, auto-updaters, cloud sync, or large abstractions until the Charge 6 probe works.