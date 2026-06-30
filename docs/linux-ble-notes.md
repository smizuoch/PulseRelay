# Linux BLE notes

Implementation notes for the Linux BLE backend. The target is the standard Linux
Bluetooth stack, not a tracker-vendor API.

## Chosen platform API

PulseRelay uses BlueZ over the system D-Bus as the Linux BLE backend.

Reasons:

- BlueZ is the standard Linux Bluetooth stack and exposes the supported
  application surface through `org.bluez` D-Bus interfaces.
- `org.bluez.Adapter1.SetDiscoveryFilter` supports service UUID filters and
  `Transport = "le"`, which matches PulseRelay's scan-first, BLE-only flow.
- `org.bluez.Device1.Connect` / `Disconnect` are the standard remote-device
  connection methods.
- Remote GATT services and characteristics are represented under the device
  object path as `org.bluez.GattService1` and `org.bluez.GattCharacteristic1`.
- Heart-rate notifications should use `org.bluez.GattCharacteristic1.StartNotify`
  and then consume `Value` property changes.

Primary references:

- BlueZ Adapter API: https://github.com/bluez/bluez/blob/master/doc/org.bluez.Adapter.rst
- BlueZ Device API: https://github.com/bluez/bluez/blob/master/doc/org.bluez.Device.rst
- BlueZ GATT Service API: https://github.com/bluez/bluez/blob/master/doc/org.bluez.GattService.rst
- BlueZ GATT Characteristic API: https://github.com/bluez/bluez/blob/master/doc/org.bluez.GattCharacteristic.rst
- BlueZ AgentManager API: https://github.com/bluez/bluez/blob/master/doc/org.bluez.AgentManager.rst
- BlueZ Agent API: https://github.com/bluez/bluez/blob/master/doc/org.bluez.Agent.rst
- D-Bus specification: https://dbus.freedesktop.org/doc/dbus-specification.html

## Current TDD boundary

`PulseRelay.LinuxBle` currently separates two responsibilities:

- `BluezHeartRateSource`: PulseRelay source lifecycle, state transitions,
  device filtering, subscription flow, and Heart Rate Measurement parsing.
- `IBluezHeartRateClient`: the D-Bus boundary that will implement BlueZ calls.

This keeps the source lifecycle testable without a real Bluetooth adapter while
leaving the real D-Bus client small and focused.

The first red/green tests cover:

- discovery starts with Heart Rate Service UUID `0000180d-0000-1000-8000-00805f9b34fb`
  and LE transport,
- non-matching advertised names are ignored,
- connect -> find 0x2A37 characteristic -> start notify,
- first notification parses through the existing core parser and transitions to
  `Streaming`,
- stop clears notify, disconnects, and stops discovery.

## Implemented D-Bus behavior

`BluezDbusHeartRateClient` is now implemented behind `IBluezHeartRateClient`.
It uses `Tmds.DBus.Protocol` only at the bus boundary.

Implemented D-Bus behavior:

1. Connect to the system bus.
2. Find the first powered `org.bluez.Adapter1` object, normally `/org/bluez/hci0`.
3. Call `SetDiscoveryFilter` with:
   - `UUIDs = [Heart Rate Service UUID]`
   - `Transport = "le"`
   - `DuplicateData = false`
4. Call `StartDiscovery`.
5. Observe `org.freedesktop.DBus.ObjectManager.InterfacesAdded` and
   `org.freedesktop.DBus.Properties.PropertiesChanged` updates for `Device1`.
   Existing device objects can gain `UUIDs`, `Name`, or `RSSI` during discovery,
   so `PropertiesChanged` is treated as discovery input, not only diagnostics.
6. Select only devices whose `UUIDs` contains Heart Rate Service and whose `Name`
   or `Alias` matches the configured name filter.
7. Call `Device1.Connect`.
8. Wait until `ServicesResolved = true`.
9. Find a `GattCharacteristic1` whose `UUID` is Heart Rate Measurement
   `00002a37-0000-1000-8000-00805f9b34fb`.
10. Call `StartNotify`.
11. Convert `Value` property updates into raw notification payloads.
12. If notification setup reports a BlueZ authorization/authentication error,
    register a temporary `NoInputNoOutput` `org.bluez.Agent1`, call
    `Device1.Pair`, unregister the agent, reacquire the characteristic, and
    retry once.

Open verification work:

- Real-device Linux logs against Fitbit Charge 6.
- Behavior when pairing/encryption needs user-visible confirmation. PulseRelay
  now registers a temporary `NoInputNoOutput` agent for app-initiated pairing;
  PIN/passkey and numeric-confirmation requests are rejected rather than
  auto-accepted. If real logs show those requirements, add a tested UI/agent
  flow instead of auto-accepting.
- Flatpak Bluetooth/D-Bus permissions after the native Linux path works outside
  Flatpak.

Manual verification checklist: [linux-ble-verification.md](linux-ble-verification.md).
