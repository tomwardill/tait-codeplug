# M0LTE.Tait.Codeplug

A Tait TM8100/TM8200 codeplug library, reverse-engineered from Free Serial Analyzer captures of the
Windows CPS. It reads and writes the codeplug over the serial programming interface without the CPS,
exposes a typed, version-pinned field map for the whole CPS Data form and the channel table, and
applies the Packet.NET (PDN) upgrade profiles. The read + write path is hardware-validated against a
real TM8100.

The CLI front-end that ships from the same repo, `tait-codeplug`, is a thin layer over this library:
see [github.com/M0LTE/tait-codeplug](https://github.com/M0LTE/tait-codeplug) for prebuilt binaries.

## The protocol, in short

- ASCII-hex, line-oriented, CR-terminated, strictly lock-step (every command gets one `>` prompt
  before the next).
- Records share the `.m8p` framing `<addr:4hex><len:2hex><data><checksum:2hex>`; checksum is the
  CCDI-family negated sum over the decoded bytes (whole record sums to 0 mod 256). `addr` is
  `(section << 8) | index`.
- Session: `^` (reset -> `v`), `#` (enter programming -> `>`), `ld` -> `{C05}`, `d00` -> `{C01}`.
  Read a section: `r<section>`. Write: `b`, `i<arg>`, a run of `w<record>`, `e`. Teardown: `^`.
- Baud opens at 9600, switches to 19200 for the transfer.

The full write-up is in [`docs/research/tait-codeplug-protocol.md`](https://github.com/packet-net/packet.net/blob/main/docs/research/tait-codeplug-protocol.md)
and the programming brief it came from is [`docs/research/tait-codeplug-programming-brief.md`](https://github.com/packet-net/packet.net/blob/main/docs/research/tait-codeplug-programming-brief.md),
both in the packet.net repo where this code started life.

## What is here

- `CodeplugChecksum` / `CodeplugRecord` / `CodeplugImage` - the record model, checksum, and .m8p
  load/save + section map. Fully offline and unit-tested.
- `Fields/` - the typed, version-pinned field map (`CodeplugFields`, `CodeplugEnums`,
  `ChannelBits`): channels (frequency, bandwidth, power, split-TX, CTCSS/DCS), the whole CPS **Data**
  form (its General, Serial Communications, RF Modems, SDM and TOTAL Transparent Mode tabs live in the
  one data/signalling record; the GPS and Customer Data tabs are separate records; plus the unit data
  identity), and audio taps. Each field is pinned by a test.
- `FieldConsole` - name/value access used by the `dump`/`get`/`set` CLI verbs.
- `CodeplugFields.ApplyPdnBasic()` / `ApplyPdnExtra()` / `ApplyPdnInternal()` - the PDN upgrade profiles (see below).
- `ISerialLine` / `SerialPortLine` - the byte seam (mirrors `Packet.Radio.Tait.ISerialIo`); tests
  substitute a scripted mock radio.
- `TaitProgrammer` - the lock-step transport state machine (connect, interrogate, read, write).

## Using it

```csharp
using M0LTE.Tait.Codeplug;

// Offline: load a CPS .m8p save and read a field.
CodeplugImage image = CodeplugImage.LoadM8p(File.ReadAllText("radio.m8p"));
CodeplugFields fields = CodeplugFields.Open(image);
Console.WriteLine(FieldConsole.Get(fields, "ch0.bandwidth"));

// Live: read the codeplug off a radio latched into programming mode (power-cycle it as you connect).
using var programmer = new TaitProgrammer(new SerialPortLine("/dev/ttyUSB0"));
CodeplugImage live = programmer.ReadImage();
```

## PDN upgrade profiles

Two composable patches that *upgrade a radio to the Packet.NET feature set* without touching its RF
config (channels, frequencies, power), so they layer safely onto a radio already provisioned for its
environment. They change only the data record (0x09). For a radio arriving from a foreign application,
prefer a clean flash of a full codeplug first, then apply a profile.

- **`pdn-basic`** enables the CCDI command channel that carries `Packet.Radio.Tait`'s telemetry and
  control: averaged/instantaneous RSSI, forward/reverse power, PA temperature, status/identity,
  transmitter keying, and the PROGRESS stream for carrier-sense (DCD) and external-PTT edges. It sets
  CCDI-mode-allowed on, power-up state to Command (so the radio is always CCDI-reachable), progress
  messages on, and the command baud to 28800.
- **`pdn-extra`** includes `pdn-basic` and adds the TNC-less internal FFSK packet modem plus the SDM
  side channel used for mode signalling: transparent mode on, **ignore-escape-sequence off** (so the
  transport can escape back to command mode - without this the radio wedges), ignore-subaudible on the
  data path, the transparent terminal baud (28800) and over-air FFSK baud (2400), and SDM + CCDI SDM
  output. The over-air baud must match at both ends; adjust the bauds and the data port for your setup.
- **`pdn-internal`** is `pdn-extra` for a radio carrying a Packet.NET internal options board (a USB
  sound-card plus serial interface on the internal options connector). On top of `pdn-extra` it sets the
  data port to Internal Options with no flow control, applies the packet audio routing (Rx tap-out R1
  split with Except-on-PTT unmute, EPTT1 tap-in T13, the same block as `audio packet-defaults`), and
  programs **IOP_GPIO1 as an active-low External PTT 1 input**, the line the board's PTT transistor pulls
  low. Unlike the other two it does change the audio block and one digital I/O line, because the board
  is nothing without them; RF configuration is still untouched.

## Programmable I/O digital lines

The Digital tab of the Programmable I/O form is record 0x37: one variable-length entry per line (a
6-bit line index, an 8-bit label length, the CPS "Pin" label as 7-bit ASCII, then 62 configuration
bits), fifteen entries on a TM8100. The 62 configuration bits are **not** mapped field by field. What
`CodeplugFields` knows are whole-line configurations lifted byte-for-byte from real CPS saves, exposed as
`DigitalIoRole`: `Unassigned` (the default for every line), `ExternalPtt1Input` (Input, External PTT 1,
active low - the configuration Tait's 3DK manual specifies for an external modem's PTT, as saved by the
CPS in the TARPN TM8105 template), and `BusyStatusOutput` (Output, Busy Status). Anything else reads as
`Other`, is preserved untouched, and cannot be written. `GetDigitalIoRole` / `SetDigitalIoRole` take a
`DigitalIoLine`; the console names them `gpio.aux_gpi1` .. `gpio.aux_gpio7`, `gpio.iop_gpio1` ..
`gpio.iop_gpio7` and `gpio.ch_gpio1`.

## Status and safety

The read + write path is hardware-validated against a real TM8100 (a same-image write round-tripped
every writable record byte-identical), and the field map is validated field-by-field against
real-radio CPS saves. Field writes are byte-identical to the CPS's own saves. Safety rails:

1. Snapshot the current codeplug before writing (the CLI's `patch` verb does this automatically).
2. Codeplug region only - this never writes firmware.
3. Version-pin: the write path refuses a radio whose database version is not in its validated set
   (currently 0094 / 0095); the field offsets are version-specific.
4. The field map enforces the CPS's own input rules (value ranges, character sets, and the "only
   available if ..." availability dependencies), so the library will not write a state the CPS rejects.
5. Bench on a sacrificial radio first, and re-read (after a power-cycle) to verify a write.

A single-record write is acked but not committed by the radio, so a live field change writes the whole
codeplug; that is the validated write path.

## Licence

AGPL-3.0-or-later. See [LICENSE](https://github.com/M0LTE/tait-codeplug/blob/main/LICENSE).
