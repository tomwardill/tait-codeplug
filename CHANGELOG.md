# Changelog

What changed in each release. The section for a version is lifted into that version's GitHub release notes by `.github/workflows/publish.yml`, so this file is the source of truth for what a release says it did.

Newest first. Add a section before tagging.

## Unreleased

- **Adding or deleting a channel now actually changes the radio.** The channel table and channel index grew and shrank correctly, but the entry counts for those two items in the item index (record 0x01) were left as they were. The radio sizes each item from that count, not from the bytes it is sent, so a two-channel write was accepted and committed and then read back as one channel: the added channel was silently dropped. Both counts now move with the channel count, the way the tone-table path already did. Bench-validated on a TM8100 (DBVer 0094): add a channel, write, power-cycle, read - byte-identical to what was sent; then delete it and the same again.

## 0.8.0 - 2026-08-21

- **`tui --driver <name>`**, and `tui --driver list` to see what your platform offers. Terminal.Gui ships three console drivers (`windows`, `ansi`, `dotnet`) and picks one for you. Since a repaint costs whatever the driver and console between them make it cost, and that varies enormously, this makes it something you can change rather than something you are stuck with.
- **`tui --bench`** times what one screen repaint actually costs on your console, because a repaint is exactly what one typed character costs. Run it per driver and use the quickest:

```
tait-codeplug tui --bench
tait-codeplug tui --bench --driver ansi
tait-codeplug tui --bench --driver dotnet
```

  It prints the screen size, the driver, and a median over 30 repaints. For scale, on Linux in tmux at 100x30 this machine gives 17.7 ms on `ansi` and 8.5 ms on `dotnet`; anything under about 30 ms feels instant, and a few hundred milliseconds is the editor feeling sluggish.

This is aimed at the report of typing being slow in the editor **on Windows, with the tool running locally**. That rules out the link, which leaves how the driver hands a repaint to the console, and on Windows that cost is far higher per call than on a Unix pty. Which of the three drivers is quickest there is not something that can be settled from a Linux box, so the tool now measures it where it matters.

## 0.7.0 - 2026-08-21

- **"Power-cycle the radio now" is a prompt, not a line in the log.** A read or a write puts it on the screen where it cannot be missed, and it takes itself back down the moment the radio answers - the normal case needs no keystroke at all. Cancel, or Esc, abandons the operation, which is the way out when the radio is not going to answer rather than sitting through the full 90-second wait.
- **Read and write show progress.** A bar and a percentage on the radio bar, from the library rather than guessed at: sections for a read, records for a write (`writing 52% (88/168)`).
- Cancelling a write is only offered up to the point where the write block opens. Past that the codeplug is being modified, and stopping half way would leave it open and partly applied, so a started write always runs to its commit.
- **The port box accepts typing again.** It is a dropdown of detected ports, and it shipped read-only, so on a machine where the radio's port does not enumerate - which a plain USB-serial cable often does not - there was no way to name one and the interactive mode could not be used at all.
- Progress redraws are throttled to a few a second. See below for why that matters more than it sounds.

**On typing being slow over SSH**, which is what prompted this release: it is real, it is measurable, and it is not something this tool can fix. Terminal.Gui repaints the entire screen for every character typed into a text box. Measured against a minimal Terminal.Gui app - one window, one text field, nothing else - so it is not something about this UI:

| terminal | per typed character |
|---|---|
| 80x24 | 13 KB |
| 100x30 | 22 KB |
| 120x40 | 37 KB |
| 200x50 | 82 KB |

That is ~7-8 bytes per cell on screen, every keystroke, and 2.4.18-develop.31 behaves identically. Locally it is invisible. On a maximised terminal over SSH it is the second or two per character that typing a frequency actually felt like. Until it is fixed upstream, three things help: a smaller terminal window while editing (80x24 is six times cheaper than 200x50), `patch <port> ch0.rxfreq 144.812500` from the command line instead of the editor, or running the tool on the machine the radio is plugged into rather than across a link.

## 0.6.1 - 2026-08-21

- **The interactive mode stops talking to the terminal when nobody is using it.** Terminal.Gui runs its main loop 25 times a second whether or not anything has changed, and rewrites cursor state every time round: sitting there with nothing happening, the tool was emitting ~315 bytes a second in 25 separate writes, for as long as it was open. The loop now steps down after ten seconds untouched and again after a minute. Measured idle output falls from 315 bytes/sec to 128 after a short pause and to 54 after a long one.
- Typing is not affected: ten keypresses measured at 29-49 ms before the change and 29-49 ms after, because ten seconds is far longer than any pause in typing. What it costs is the single keypress that wakes it up after a long pause, measured over four attempts at 248, 60, 235 and 70 ms - up to a quarter of a second, once, and everything after it is back to normal.

This is a candidate fix for "the UI goes laggy after a few minutes", not a confirmed one, and it is worth being straight about which. On a local terminal the lag does not reproduce: twelve minutes idle held latency flat at 44-73 ms, CPU at 2.1% and file handles constant, and 150 open-and-close cycles of the channel editor held latency flat at ~60 ms with no handle growth. What the tool was doing wrong regardless is the constant output, which over SSH is 25 packets a second the far end can never stop servicing. If it still goes laggy, the thing to say is which terminal and what connection - SSH, tmux, mosh, Windows Terminal - because that is where the remaining suspects live.

## 0.6.0 - 2026-08-21

- **Keyboard navigation between panels.** `F6` (and `Shift+F6`) moves between Radio, Channels, PDN preset and Log; `Tab` moves within a panel. The panel holding the keyboard now lights its border white, so where you are is visible rather than guesswork. Previously `Tab` could not leave the Radio panel at all, which also meant `Enter` on the channel list never fired: the list could not be reached.
- **Add and delete channels.** `F7` adds a channel and opens it for editing; `F8` deletes the selected one after a confirmation. There are buttons for both, and `channel add <file.m8p>` / `channel delete <file.m8p> <n>` do the same from the command line.
- A new channel starts as a copy of the one before it, because a zeroed channel is 0 Hz at power Off, which is never what you want.
- Deleting a channel shifts the ones above it down and clears a GPS poll-response channel left pointing past the end, which is exactly what the CPS rejects on load.
- **The serial port is a dropdown** of detected ports with a Rescan button, instead of a box you had to know what to type into. A port that did not enumerate can still be typed in.
- The log is capped at 500 lines so a session left open all day cannot grow it without bound.

Adding or removing a channel changes the codeplug's shape, so it is worth saying what it is pinned against: growing the CPS's own 1-channel default file to 2 and to 6 channels reproduces, byte for byte, the channel index table and the record chunking found in a real 2-channel radio readout and a real 6-channel CPS save. Nine tests hold that. It has not yet been written to a radio or loaded back into the CPS.

## 0.5.0 - 2026-08-21

- **`tait-codeplug --upgrade`**: fetch the latest release for this platform and replace the running binary in place. No more download-and-chmod to move up a version.
- The download is verified against the release's own `SHA256SUMS` and discarded on a mismatch, so nothing unaccounted-for is ever installed.
- The swap is an atomic rename, and the existing file mode is preserved, so a failure at any point leaves the working binary exactly as it was.
- Refuses early, in under a tenth of a second, when it cannot write where the binary lives, rather than pulling 40 MB down first to then fail. Points at `sudo` when the directory is system-owned.
- Refuses to replace a renamed copy or a build-tree binary.

## 0.4.1 - 2026-08-20

- The interactive mode is in colour instead of Terminal.Gui's stock grey-on-black: a dark slate palette with a blue accent on panel borders and titles.
- Green for read, amber for write because it is the one that changes your radio, red for the error dialog.
- The port box sits on an inset background so it reads as somewhere to type, and the status line turns green once a codeplug is loaded.
- Rounded panel borders; the window title carries the version.
- An empty channel pane now tells you to press F5 rather than showing a blank box.
- Colours are 24-bit and map down automatically on a 16- or 256-colour terminal.

## 0.4.0 - 2026-08-20

- **Interactive mode, and it is what you get when you run the tool with no arguments**: a serial port selector, the channel table (frequency, bandwidth, power), a PDN preset picker, and read/write buttons.
- `F5` reads the radio, `F3` or Enter edits the selected channel, `F2` writes back, `F10` quits.
- The PDN preset is staged rather than applied on selection, so choosing `pdn-basic` or `pdn-extra` changes nothing until you write.
- A write always snapshots the pre-change codeplug to a backup file first, the same rule the `patch` verb follows.
- The radio work runs off the UI thread, so the screen stays live through the ~25s read and the 90s the connect spends waiting for your power-cycle, with a log pane narrating.
- `tait-codeplug tui [file.m8p]` opens the same screen on a saved codeplug, so the editor can be used without a radio on the bench.
- `--help` / `-h` / `help` print usage and exit 0; a no-argument run with redirected output still prints usage rather than trying to draw a UI at a pipe.

## 0.3.0 - 2026-08-20

First release from this repository. The tool and its library moved here from [`packet-net/packet.net`](https://github.com/packet-net/packet.net), with their history, and continue that version numbering.

- The library is now published to nuget.org as [`M0LTE.Tait.Codeplug`](https://www.nuget.org/packages/M0LTE.Tait.Codeplug), so it can be consumed without vendoring the source.
- The CLI project and namespace are renamed to match; the shipped command is still `tait-codeplug`.
- Same six self-contained, single-file binaries as before: linux-x64 / arm64 / arm, win-x64, osx-x64 / arm64.

Carried over from the work done in packet.net, and what the tool can do as of this release:

- Read and write a TM8100 / TM8200 codeplug over the serial programming interface without the Windows CPS. Hardware-validated: a same-image write round-trips every writable record byte-identical.
- `parse` / `dump` / `get` take their source from either an `.m8p` file or a serial port, so the decode verbs work against a live radio.
- The whole CPS **Data** form is mapped and typed (General, Serial Communications, RF Modems, SDM, Transparent Mode, GPS, Customer Data), plus the channel table: frequency, bandwidth, power, split TX, squelch, TX inhibit, network, and full CTCSS/DCS read and write.
- The `pdn-basic` and `pdn-extra` upgrade profiles configure a radio for the Packet.NET feature set without touching its RF or channel config.
- Writes are version-pinned to a validated database version and refuse anything else; `patch` backs up the pre-change codeplug before writing; the raw whole-file write verb is deliberately absent.
- The field map enforces the CPS's own input rules and "only available if" dependencies, sourced from the manual, so the tool will not write a state the CPS rejects.
