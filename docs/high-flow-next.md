# Aqua Computer high flow NEXT: RGB and settings protocol

Notes on how the **Aqua Computer high flow NEXT** flow sensor (USB `0C70:F012`) takes its RGB and display settings, found while building [RGB Studio](../README.md). Everything below was checked on one sensor: every report read back closes its CRC, and the writes listed here changed the LEDs as described.

*Notas em inglês para servirem a quem programa em qualquer língua. Em resumo: a configuração do sensor é o relatório HID `0x03`, de 682 bytes, big-endian e com CRC-16/USB no fim. As luzes são 8 controladores de 70 bytes a partir de `0x05C`.*

> Unofficial. Writing settings to a device you depend on for cooling is at your own risk. Change only what you understand and always keep the CRC valid.

## HID reports

From the device's HID descriptor:

| Type | ID | Size | Use |
|---|---|---|---|
| Input | `0x01` | 163 B | sensor readings (offsets in the Linux `aquacomputer_d5next` driver) |
| Output | `0x02` | 10 B | commands: `[02][8 bytes][CRC-16/USB of the 8 bytes, big-endian]` |
| Feature | `0x03` | 681 B | **settings** (682 with the report ID) |
| Feature | `0x08` | 772 B | not identified |
| Feature | `0x0C` | 1024 B | display-sized; a GET returns report `0x03`, a SET is accepted but nothing shows |

On Windows, `HidD_GetFeature` / `HidD_SetFeature` take the full feature buffer (1025 bytes) and the output report its own length (11 bytes, sent with `HidD_SetOutputReport`). Sending the save command as a feature report fails with error 87.

## Settings report (`0x03`)

Big-endian, packed (`Pack=1`), the structure starting right after the report ID. Read it, change bytes, recompute the CRC, write it back.

- **Checksum:** CRC-16/USB (poly `0x8005` reflected = `0xA001`, init `0xFFFF`, xorout `0xFFFF`) over bytes `0x001`–`0x2A7`, stored big-endian at `0x2A8`.
- **Applying and saving:** a SET applies at once and lasts until power loss. To keep it, send the save command `02 00 00 00 02 00 00 00 00 34 C6` as an output report.
- **Busy after a write:** right after a SET the device does not answer reads for a moment (error 31); wait ~300 ms.
- **Do not flood it:** streaming full settings at 35-56/s while aquasuite also talked to the device stopped it answering feature reports until it was replugged. 30/s for a few seconds was fine; RGB Studio stays at 20/s.

| Offset | Size | Field |
|---|---|---|
| `0x001` | u16 | structure ID, `1` |
| `0x003` | u8 | temperature unit |
| `0x004` | u8 | flow unit |
| `0x005` | u16 | display page time, seconds |
| `0x007` | u32 ×2 | display pages enabled (bits 0-15 at `0x009`-`0x00A`, see below) |
| `0x00F` | u8 | display brightness |
| `0x010` | u8 | display idle brightness |
| `0x011` | u16 | display idle timeout |
| `0x013` | u16 | menu timeout |
| `0x015` | u8 | display flags |
| `0x016` | 4 × (s16, u16) | charts 1-4: data source, log interval |
| `0x026` | u16 | flags |
| `0x028` | s16 | USB current limit, mA |
| `0x02A` | u8 | aquabus address (`58`) |
| `0x02B` | s16 ×2 | water and external temperature offsets |
| `0x02F` | 42 B | flow setup: medium, connector, calibration tables |
| **`0x059`** | u8 | **LED brightness, whole device** (0-255) |
| `0x05A` | u16 | LED strip flags |
| **`0x05C`** | 8 × 70 B | **LED controllers** (below) |
| `0x28C` | u16 | standby trigger |
| `0x28E` | … | conductivity, water quality, power sensor, alarms |
| `0x2A7` | u8 | profile ID |
| `0x2A8` | u16 | CRC-16/USB |

### Display pages

Bits 0-15 of the page flags, in the order aquasuite shows them: 0 device info, 1 flow, 2 water temperature, 3 external temperature, 4 conductivity, 5 water quality, 6 volume counter, 7 power, 8 flow + water temperature, 9 conductivity + quality, 10 temperatures, 11 flow + volume, 12-15 charts 1-4. aquasuite writes the whole report on every click.

## LED controllers

Eight controllers of 70 bytes, at `0x05C + 70 × n`. Each lights `count` LEDs from `led_start` on one strip with one of the device's own effects. Strip 1 is the sensor's own ring; strip 0 is the RGBpx output.

| +Offset | Size | Field |
|---|---|---|
| `+0` | u8 | strip: `0` RGBpx output, `1` the ring |
| `+1` | u8 | first LED |
| `+2` | u8 | LED count |
| `+3` | u8 | mode (below) |
| `+4` | u16 | flags |
| `+6` | s16 + 2 B | data source (`-1` none) and filter |
| `+10` | s16, s16, u8, u8 | binding 1: x1, x2, y1, y2 |
| `+16` | 6 B | binding 2 |
| `+22` | s16 ×12 | effect parameters (speed, width…, per mode) |
| `+46` | 6 × (u16, u8, u8) | colors: hue 0-1535, saturation, value. Slot 0 is the background |

Hue: 0 red, 128 orange, 256 yellow, 512 green, 768 aqua, 1024 blue, 1280 purple, 1535 max. On this ring, saturation below 255 or hue above 1024 already looks purple next to green.

### Modes

| Value | Mode | | Value | Mode |
|---|---|---|---|---|
| 0 | off | | 12 | ambient |
| 1 | fixed color | | 13 | bar |
| 2 | breathing | | 14 | candle |
| 3 | rainbow | | 15-17 | rain 1-3 |
| 4, 5 | blink 1, 2 | | 18 | switch |
| 7 | sequential | | 19 | rainbow shift |
| 8, 9 | visor 1, 2 | | 20-23 | sound 1-4 |
| 10 | marquee 1 ("Wave" in aquasuite) | | 24 | ambient px |
| 11 | marquee 2 | | 33 | gradient |

## Per-LED color from the PC

The device has no per-LED stream: aquasuite's structures carry only the settings above and eight software-sensor values. To draw arbitrary frames, RGB Studio lays the 8 controllers over the ring, each `fixed color` on 1-2 LEDs (starts `0, 2, 3, 4, 5, 7, 8, 9`, 11 positions; with 10 one LED stayed dark), and resends the settings for each frame, never saving. Only the lighting bytes `0x059`-`0x28B` ever differ from what the device holds.

## Not known yet

- What each of the 12 effect parameters means per mode, and the controller flag bits.
- Feature report `0x08`.
- The command that asks for a screenshot through report `0x0C`.
- The software-sensor report and the chart source values that point a chart at one.

The C# implementation is [`RgbStudio/Core/HighFlowNext.cs`](../RgbStudio/Core/HighFlowNext.cs) and [`HighFlowNextBackend.cs`](../RgbStudio/Core/HighFlowNextBackend.cs). Tests with a real settings report are in [`RgbStudio.Tests/HighFlowNextTests.cs`](../RgbStudio.Tests/HighFlowNextTests.cs).
