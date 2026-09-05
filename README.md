# tait-codeplug

Read, decode, edit and program a **Tait TM8100 / TM8200** codeplug over the serial programming
interface, on Linux, macOS or Windows, without the Windows CPS.

Reverse-engineered from Free Serial Analyzer captures of the CPS and hardware-validated against a
real TM8100: a same-image write round-trips every writable record byte-identical, and the field map
is validated field-by-field against real-radio CPS saves.

Two things ship from this repo, at the same version:

| | |
|---|---|
| **`tait-codeplug`** | the CLI, as a self-contained single-file binary for six platforms - [latest release](https://github.com/M0LTE/tait-codeplug/releases/latest) |
| **`M0LTE.Tait.Codeplug`** | the library behind it, on [nuget.org](https://www.nuget.org/packages/M0LTE.Tait.Codeplug) |

## Install the CLI

Each binary embeds the .NET runtime and the native serial library, so there is nothing else to
install. Grab the one for your platform from the [latest release](https://github.com/M0LTE/tait-codeplug/releases/latest):

```sh
curl -LO https://github.com/M0LTE/tait-codeplug/releases/latest/download/tait-codeplug-<version>-linux-x64
chmod +x tait-codeplug-<version>-linux-x64
./tait-codeplug-<version>-linux-x64
```

Assets: `linux-x64`, `linux-arm64`, `linux-arm` (armv7 / 32-bit Pi), `win-x64`, `osx-x64` (Intel),
`osx-arm64` (Apple Silicon). `SHA256SUMS` covers every asset.

Or build it yourself: `dotnet run --project src/M0LTE.Tait.Codeplug.Cli -- <verb> ...` (.NET 10 SDK).

## Interactive mode

Run it with no arguments and you get a screen instead of a verb: pick a port, read the radio, edit the packet-relevant essentials, write it back.

```
╭┤tait-codeplug 0.4.1 - Tait TM8100/TM8200 codeplug editor├────────────────────────────────╮
│╭┤Radio├─────────────────────────────────────────────────────────────────────────────────╮│
││ Port:                           (no serial ports detected)                             ││
││                                                                                        ││
││ ⟦ Read from radio ⟧▖ ⟦ Write to radio ⟧▖ DBVer 0095, 169 records                       ││
│╰────────────────────────────────────────────────────────────────────────────────────────╯│
│╭┤Channels - Enter or F3 to edit├────────────────────────────╮╭┤PDN preset├──────────────╮│
││ #   RX (MHz)      TX (MHz)      Bandwidth  Power           ││ ◉ none                   ││
││ 0   144.812500    (= RX)        Narrow     High            ││ ○ pdn-basic              ││
││                                                            ││ ○ pdn-extra              ││
││ ○ pdn-internal           ││
││                                                            ││                          ││
││                                                            ││ Applied when you         ││
││                                                            ││ write. Neither preset    ││
││                                                            ││ touches RF or channel    ││
││                                                            ││ config.                  ││
│╰────────────────────────────────────────────────────────────╯╰──────────────────────────╯│
│╭┤Log├───────────────────────────────────────────────────────────────────────────────────╮│
││ 22:41:05  loaded /home/tf/packet.net/tait-programming-research/tait-gps-customer-identi││
││ 22:41:05  DBVer 0095, 1 channel(s) decoded.                                            ││
││ 22:41:11  channel 0 edited (not yet written to the radio).                             ││
││                                                                                        ││
││                                                                                        ││
││                                                                                        ││
││                                                                                        ││
│ F10  Quit │ F3  Edit channel │ F5  Read │ F2  Write                                      │
╰──────────────────────────────────────────────────────────────────────────────────────────╯
```

`F6` moves between panels and `Tab` moves within one; the panel holding the keyboard lights its border. `F5` reads the radio (it prompts you to power-cycle it), `F3` edits the selected channel, `F7` adds one, `F8` deletes one, `F2` writes back, `F10` quits. The PDN preset is staged and applied when you write, so choosing one changes nothing until you commit. A write always snapshots the pre-change codeplug to a `tait-codeplug-backup-<timestamp>.m8p` first.

The radio work runs off the UI thread, so the screen stays live through the ~25s read and the 90s the connect will wait for your power-cycle.

Left alone, it goes quiet: the main loop steps down after ten seconds untouched and again after a minute, so an editor left open over SSH is not writing to your terminal 25 times a second all afternoon. Typing is unaffected; the one key that wakes it after a long pause can take up to a quarter of a second to register, and everything after it is normal.

`F5` and `F2` put "power-cycle the radio now" on the screen rather than in the log, and take it down again by themselves once the radio answers. Cancel or Esc abandons the operation instead of waiting out the full 90 seconds. Both show a progress bar while they run.

### Typing feels slow over SSH

It is, and it is worth knowing why before you go looking for a fault at your end. Terminal.Gui repaints the whole screen for every character typed into a text box - about 7-8 bytes per cell on screen, so 22 KB on a 100x30 terminal and 82 KB at 200x50, per keystroke. A minimal Terminal.Gui app does the same, so it is the library rather than this tool, and there is nothing to configure around it.

How much that costs you depends on the console and on which of Terminal.Gui's three drivers is in front of it, and the difference between them is large. Measure it on your own machine rather than trusting a number from someone else's:

```sh
tait-codeplug tui --driver list      # what this platform offers
tait-codeplug tui --bench            # time one repaint on the default driver
tait-codeplug tui --bench --driver ansi
```

Under about 30ms per repaint feels instant; a few hundred milliseconds is the editor feeling sluggish. If another driver is quicker, use it: `tait-codeplug tui --driver ansi radio.m8p`.

Beyond that:

- Make the terminal window smaller while you are editing: 80x24 costs a sixth of what 200x50 does.
- Skip the editor for a single value: `tait-codeplug patch /dev/ttyUSB0 ch0.rxfreq 144.812500` does a read-modify-write with no typing in a UI at all.
- Over SSH, run the tool on the machine the radio is plugged into rather than across the link.

Colours are true-colour: a dark slate palette, green for read, amber for write (it is the one that changes your radio), red for errors. Terminal.Gui maps them down on a 16- or 256-colour terminal, so it stays legible on a plain console.

To try the editor without a radio on the bench, open a saved codeplug: `tait-codeplug tui radio.m8p`.

## Use it from the command line

```
# decode - the source is an .m8p file OR a serial port (reads the live radio)
tait-codeplug parse   <file.m8p | port>            verify checksums + print the section map
tait-codeplug dump    <file.m8p | port>            decode every mapped field
tait-codeplug get     <file.m8p | port> [field]    read one field, or all as name=value
tait-codeplug set     <file.m8p> <field> <value>   set one field and save (e.g. ch0.bandwidth Wide)
tait-codeplug set     <file.m8p> profile <name>    apply a PDN upgrade profile to a file

# hardware (radio latched into programming mode on <port>: power-cycle it as you trigger)
tait-codeplug version <port>                       interrogate: model / firmware / serial
tait-codeplug read    <port> [out.m8p]             read the codeplug (to a file, or stdout if omitted)
tait-codeplug patch   <port> <field> <value>       live-set one field (backs up first)
tait-codeplug patch   <port> profile <name>        live-apply a PDN upgrade profile
tait-codeplug channel add    <file.m8p>            append a channel (a copy of the last one)
tait-codeplug channel delete <file.m8p> <n>       remove channel n, shifting the rest down
tait-codeplug tui     [file.m8p]                  interactive mode, optionally on a saved codeplug
tait-codeplug --upgrade                           replace this binary with the latest release
```

`--upgrade` fetches the release build for your platform, checks it against the release's own `SHA256SUMS`, and renames it over the running binary. Nothing is replaced unless the checksum matches, and the swap is a rename, so a failure at any point leaves what you have working. It refuses early if it cannot write where the binary lives, rather than downloading 40 MB first to find out.

The radio must be latched into programming mode: power-cycle it as the command connects. Progress and
prompts go to stderr, so `read <port> > radio.m8p` gives you a clean `.m8p` on stdout.

## PDN upgrade profiles

`pdn-basic`, `pdn-extra` and `pdn-internal` upgrade a radio to the [Packet.NET](https://github.com/packet-net/packet.net)
feature set - CCDI telemetry and control, and the TNC-less internal FFSK packet modem plus SDM mode
signalling - **without touching RF config** (channels, frequencies, power), so they layer safely onto a
radio already provisioned for its environment. See the
[library README](src/M0LTE.Tait.Codeplug/README.md#pdn-upgrade-profiles) for exactly what each one sets.

`pdn-internal` is the one for a radio with a Packet.NET internal options board fitted: `pdn-extra` plus
the data port on Internal Options, the audio taps for a sound-card modem (R2 out, unmuted except on
PTT, T13 in), and IOP_GPIO1 programmed as an active-low External PTT 1 input for the board's PTT line. The PTT line is also settable on its own:
`set radio.m8p gpio.iop_gpio1 ExternalPtt1Input` (or `Unassigned`, or `BusyStatusOutput` on a line that
can be an output); `get radio.m8p | grep gpio` lists every line.

## Safety

1. `patch` snapshots the current codeplug to a backup file before writing. Keep it.
2. Codeplug region only. This never writes firmware.
3. Version-pinned: the write path refuses a radio whose database version is not in its validated set
   (currently 0094 / 0095), because the field offsets are version-specific.
4. The field map enforces the CPS's own input rules, so the tool will not write a state the CPS rejects.
5. Bench on a sacrificial radio first, and re-read after a power-cycle to verify a write.

No RF is involved in any of this, and no part of it transmits.

## Protocol and provenance

The protocol write-up is [`docs/research/tait-codeplug-protocol.md`](https://github.com/packet-net/packet.net/blob/main/docs/research/tait-codeplug-protocol.md)
and the programming brief is [`docs/research/tait-codeplug-programming-brief.md`](https://github.com/packet-net/packet.net/blob/main/docs/research/tait-codeplug-programming-brief.md),
both in [packet-net/packet.net](https://github.com/packet-net/packet.net), where this code was
developed before moving here. Its history came with it. That repo also holds `Packet.Radio.Tait`, the
runtime CCDI/transparent-mode driver these profiles provision a radio for.

## Releasing

Add a section to [`CHANGELOG.md`](CHANGELOG.md) for the version first: its bullets become the "What's changed" list at the top of the GitHub release, above the install instructions. If you forget, the release falls back to the commit subjects since the previous tag and the run logs a warning.

A `v*` tag runs [`.github/workflows/publish.yml`](.github/workflows/publish.yml): it gates on the test
suite, pushes `M0LTE.Tait.Codeplug` to nuget.org via trusted publishing (OIDC, no stored API key), then
cross-publishes the six CLI binaries and attaches them plus `SHA256SUMS` to a GitHub Release.

```sh
git tag -a v0.3.0 -m "v0.3.0 - <one-line summary>" && git push origin v0.3.0
```

## Licence

AGPL-3.0-or-later. See [LICENSE](LICENSE).
