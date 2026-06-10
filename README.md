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

A small plugin runs inside the game and impersonates the real USB Toy Pad, so
the game thinks a portal is connected. The app never talks to the game
directly — everything goes through **FTP**:

1. A figure is represented by a 180-byte NFC tag dump (`.bin`).
2. Each of the 7 pad spaces maps to a file (`figure1.bin` … `figure7.bin`).
3. To **place** a figure, the app uploads its `.bin` as that space's file. To
   **remove** it, the app deletes the file. The plugin watches that folder and
   reports the change to the game as if a real figure was tapped.
4. The plugin also writes a small `leds.txt` mirror with the panel colors the
   game is driving; the app reads it over FTP to animate the on-screen pad.

Nothing about the figures leaves your device except this local FTP transfer to
your own console. Figure `.bin` dumps are game content and are **not
distributed** — each user imports their own (see [Import figures](#usage)).

## Console layout and FTP

| | PS3 | PS4 |
|---|---|---|
| FTP server | webMAN MOD | GoldHEN FTP |
| FTP port | `21` | `2121` |
| Figures folder | `/dev_hdd0/tmp` | `/data/toypad_emu` |
| Per-space files | `figure1.bin` … `figure7.bin` | same |
| LED mirror | `/dev_hdd0/tmp/leds.txt` | `/data/toypad_emu/leds.txt` |

## What "Install" does

The **Install** button uploads the plugin (and anything else the console needs)
over FTP, fully automatically. You don't copy files by hand.

### PS3 (Install PS3)

1. Uploads the plugin `toypad_emu.sprx` to `/dev_hdd0/tmp/toypad_emu.sprx`.
2. Backs up the game's original executable **once**:
   `/dev_hdd0/game/BLUS31473/USRDIR/EBOOT.BIN` → `EBOOT.BIN.original`
   (skipped if a backup already exists, so it never overwrites your original).
3. Uploads the patched `EBOOT.BIN` in its place. The patch makes the game load
   the plugin on launch.
4. Restart the game.

To revert, delete the patched `EBOOT.BIN` and rename `EBOOT.BIN.original` back.

### PS4 (Install PS4)

1. Creates `/data/GoldHEN/plugins/` if missing.
2. Uploads the plugin `toypad_emu.prx` to
   `/data/GoldHEN/plugins/toypad_emu.prx`.
3. Registers it in `/data/GoldHEN/plugins.ini` under the game's section,
   adding this line (only if it isn't there yet — your other plugins are kept):

   ```ini
   [CUSA00935]
   /data/GoldHEN/plugins/toypad_emu.prx
   ```
4. Creates the figures folder `/data/toypad_emu/`.
5. Reboot or reload GoldHEN, then start the game.

## Requirements

| | PS3 | PS4 |
|---|---|---|
| Jailbreak | HEN 4.91+ or CFW | GoldHEN (Plugin Loader) |
| FTP | webMAN MOD enabled | GoldHEN FTP enabled |
| Game | BLUS31473 (US), updated to v1.22 | CUSA00935 (US), updated to **v1.23** |

> The plugin and EBOOT patch target the **US** game IDs above. Other regions are
> not supported out of the box.

## Usage

1. **Pick your console** — asked on first run, or use the **Mode: PS3 / PS4**
   toggle in the toolbar.
2. **Import figures** — load your `Dimensions.zip` containing the NFC dumps
   (`.bin`) organized into `Characters`, `Vehicles` and `Gadgets` folders. Files
   stay on the device and are never uploaded anywhere.
3. **Connect** — enable FTP on the console and enter its local IP (and FTP user
   / password if you set one). The app uses port `21` for PS3 and `2121` for
   PS4 automatically.
4. **Install** — tap **Install PS3 / Install PS4** once. This does everything in
   [What "Install" does](#what-install-does). Then restart the game.
5. **Place figures** — drag a figure onto a space, or tap a space then a figure.
   Drag between spaces to move or swap; tap a filled space to remove.

### Vehicles and gadgets

They appear generic (blank) until built, just like real figures. Build one
in-game, then **remove it from the pad or tap Sync** — the app downloads the
built tag from the console and saves it over your local file, so it shows up
complete next time. Works on both PS3 and PS4.

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
unsigned (an Apple certificate is required to install it). Tagging a release as
`vX.Y` triggers the release job, which publishes the APK, IPA and Windows build.

## Credits

- **Development:** RadiantDelux — plugin, EBOOT patch, protocol and interface.
- **Protocol:** based on node-ld / ToyPadEmu.
- **Tools:** TrueAncestor SELF Resigner, scetool, SPRXPatcher (mod), Cell SDK,
  webMAN MOD, GoldHEN, OpenOrbis SDK.
- **Images:** LEGO Dimensions Wiki (Fandom).

LEGO and LEGO Dimensions are trademarks of The LEGO Group. This is an
unaffiliated, non-profit project for preservation and personal use.
