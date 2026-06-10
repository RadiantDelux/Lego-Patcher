# LEGOPATCHER

Software emulator of the LEGO Dimensions **Toy Pad** for **PS3 and PS4**. Pick
characters, vehicles and gadgets in the app and they appear on the console
instantly — no physical portal and no real figures.

Cross-platform .NET MAUI app for **Windows, Android and iOS**.

## Features

- Place, move, swap and remove figures across all 7 pad spaces.
- **Live light mirror:** the app shows each panel (left / center / right)
  lighting up with the colors the game drives, following the real pad's shape.
- **Auto-saved builds:** vehicles and gadgets you build in-game are captured and
  saved locally, so they show up already built next time.
- **PS3 and PS4** support, selectable in the app.
- **English and Spanish** UI, switchable at any time.

## How it works

A plugin runs inside the game and pretends to be the real USB portal. The app
talks to it over FTP and tells it which figure to place on each space.

| Console | FTP | Install target |
|---------|-----|----------------|
| **PS3** | webMAN MOD, port 21 | `.sprx` plugin + patched `EBOOT.BIN` (original backed up) |
| **PS4** | GoldHEN, port 2121 | `toypad_emu.prx` in `/data/GoldHEN/plugins/`, registered in `plugins.ini` |

Figure `.bin` dumps are **not distributed** — they are game content. Each user
imports their own `Dimensions.zip`; files stay on the device and are never
uploaded anywhere.

## Requirements

| | PS3 | PS4 |
|---|---|---|
| Jailbreak | HEN 4.91+ or CFW | GoldHEN (Plugin Loader) |
| FTP | webMAN MOD enabled | GoldHEN FTP enabled |
| Game | BLUS31473 (US), v1.22, folder or ISO | CUSA00935, updated to 1.23 |

## Usage

1. **Pick your console** — asked on first run, or use the **Mode: PS3 / PS4**
   toggle in the toolbar.
2. **Import figures** — load your `Dimensions.zip` (Characters / Vehicles /
   Gadgets `.bin` dumps).
3. **Connect** — enable FTP on the console, then enter its local IP.
4. **Install** — tap **Install PS3 / Install PS4** to upload the plugin (and on
   PS3 the patched EBOOT), then restart the game.
5. **Place figures** — drag a figure onto a space, or tap a space then a figure.
   Drag between spaces to move or swap; tap a filled space to remove.

### Vehicles and gadgets

They appear generic (blank) until built, just like real figures. Build one
in-game, then **remove it from the pad or tap Sync** — the app downloads the
built tag and saves it over your local file, so it shows up complete next time.

## Languages

The UI ships in **English and Spanish**. The language picker appears on first
launch, and you can switch anytime with the **EN / ES** button in the toolbar
(your choice is remembered).

## Build

Local (Windows):

```
dotnet build -c Release -f net10.0-windows10.0.19041.0
```

Other platforms build in GitHub Actions (see `.github/workflows/build.yml`):
Android on the Ubuntu runner, iOS on the macOS runner. The `.ipa` comes out
unsigned (an Apple certificate is required to install it).

## Credits

- **Development:** RadiantDelux — plugin, EBOOT patch, protocol and interface.
- **Protocol:** based on node-ld / ToyPadEmu.
- **Tools:** TrueAncestor SELF Resigner, scetool, SPRXPatcher (mod), Cell SDK,
  webMAN MOD, GoldHEN, OpenOrbis SDK.
- **Images:** LEGO Dimensions Wiki (Fandom).

LEGO and LEGO Dimensions are trademarks of The LEGO Group. This is an
unaffiliated, non-profit project for preservation and personal use.
