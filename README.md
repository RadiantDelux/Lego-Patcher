# LEGOPATCHER

Software emulator of the LEGO Dimensions **Toy Pad** for **PS3 and PS4**. Place
characters, vehicles and gadgets in the game without the physical portal or real
figures — pick them in the app and they appear on the console instantly.

Cross-platform .NET MAUI app: **Windows, Android, iOS and macOS**.

## How it works

A plugin runs inside the game on the console and pretends to be the real USB
portal. The app tells the plugin, over FTP, which figure to place on each space.

- **PS3:** plugin via webMAN MOD FTP (port 21); installs the `.sprx` + a patched
  `EBOOT.BIN` (the original is backed up).
- **PS4:** plugin via GoldHEN FTP (port 2121); uploads `toypad_emu.prx` to
  `/data/GoldHEN/plugins/` and registers it in `plugins.ini` under `[CUSA00935]`.

The app also mirrors the **Toy Pad lights**: it reads the colors the game drives
and lights up the three panels (left / center / right) live, following the real
pad's shape.

## Requirements

**PS3**
- Jailbroken PS3 (HEN 4.91+ or CFW) with **webMAN MOD** and FTP enabled.
- Game installed as folder or ISO, **version 1.22**, **US edition (BLUS31473)**.

**PS4**
- PS4 with **GoldHEN** (Plugin Loader) and its FTP server enabled.
- LEGO Dimensions **CUSA00935**.

## Usage

1. Pick your console: the app asks **PS3 or PS4** on first run, or use the
   **Mode: PS3 / PS4** toggle in the toolbar.
2. Enable FTP on the console.
3. **Connect** → enter the console's local IP.
4. **Install PS3 / Install PS4** → uploads the plugin (and on PS3 the patched
   EBOOT). Restart the game afterwards.
5. Place figures: drag one onto a space, or tap a space then a figure. Drag
   between spaces to move or swap; tap a filled space to remove.

The figure `.bin` dumps are **not distributed** (game content). Each user
imports their own `Dimensions.zip`; files are stored only on the device and
never uploaded anywhere.

## Languages

The interface is available in **English and Spanish**. Switch anytime with the
**EN / ES** button in the toolbar (it remembers your choice).

## Build

Local (Windows):
```
dotnet build -c Release -f net10.0-windows10.0.19041.0
```

Other platforms build in GitHub Actions (see `.github/workflows/build.yml`):
Android on the Ubuntu runner, iOS/macOS on the macOS runner. The `.ipa`/`.app`
come out unsigned (you need your own Apple certificate to install).

## Credits

- Development: **RadiantDelux** — plugin, EBOOT patch, protocol and interface.
- Protocol: based on node-ld / ToyPadEmu.
- Tools: TrueAncestor SELF Resigner, scetool, SPRXPatcher (mod), Cell SDK,
  webMAN MOD, GoldHEN, OpenOrbis SDK.
- Images: LEGO Dimensions Wiki (Fandom).

LEGO and LEGO Dimensions are trademarks of The LEGO Group. Unaffiliated,
non-profit project, for preservation and personal use.
