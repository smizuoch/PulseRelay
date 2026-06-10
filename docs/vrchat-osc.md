# OSC output and VRChat conventions

`PulseRelay.Osc` sends one OSC message per heart-rate sample over UDP.

Reference: <https://docs.vrchat.com/docs/osc-overview>

## Defaults

| Setting | Default | CLI flag |
|---|---|---|
| Host | `127.0.0.1` | `--osc-host` |
| Port | `9000` (VRChat's default receive port) | `--osc-port` |
| Address | `/avatar/parameters/VRCOSC/Heartrate/Value` | `--osc-address` |
| Type | int32 (`,i`), value = BPM | (fixed) |

The VRChat-specific values live only in these defaults — the OSC module itself is a
generic OSC 1.0 encoder/sender.

## Wire format

Each datagram is a single OSC message:

```
address (ASCII, null-terminated, padded to 4-byte boundary)
",i"    (type tag string, null-terminated, padded to 4 bytes)
int32   (big-endian BPM)
```

Example for BPM = 80 (hex):

```
2f6176617461722f706172616d65746572732f5652434f53432f4865617274726174652f56616c7565 0000 00   address + padding (44 bytes)
2c69 0000                                                                                      ",i" + padding
0000 0050                                                                                      80, big-endian
```

The encoder is byte-exact unit-tested (`OscWriterTests`).

## Monitoring without VRChat

Any UDP listener on port 9000 works, e.g.:

```sh
# macOS / Linux
nc -ul 9000 | xxd
```

or run an OSC debug tool (Protokol, OSCmon, etc.) listening on 9000.

## VRChat notes

- VRChat receives on port 9000 on localhost by default; OSC must be enabled in the
  VRChat action menu (Options → OSC → Enabled).
- The default address targets a `VRCOSC/Heartrate/Value`-style int avatar parameter;
  the avatar must actually have a matching parameter for anything to react. Use
  `--osc-address` to match whatever parameter your avatar expects.
- Sending is fire-and-forget UDP; PulseRelay logs a warning on send failure but never
  blocks or drops the BLE stream because of OSC problems.
