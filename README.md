# Way of the Samurai 4 Launcher

An unofficial Windows launcher for graphics options, Alt+Tab protection, a loading-screen warning, and a tested 60 FPS option.

## Quick start

1. Install the game from Steam and sign in to the Steam client. Start the game once, then close it.
2. Download [Steamless](https://github.com/atom0s/Steamless). Open it, select the game's `WayOfTheSamurai4.exe`, and unpack it. Steamless creates an unpacked executable beside the original. Use Steamless only with a game you own, and do not share the unpacked game executable.
3. Download or clone this project. Keep all files together and run `WOTS4-Launcher.exe`. Select the game folder if it is not found automatically.
4. Click **Enable 60 FPS** and select the unpacked executable when asked. The launcher prepares the tested 60 FPS patch and matching stock 30 FPS files. The button then switches between 60 FPS and **Restore 30 FPS**. Close the game first.
5. Choose graphics settings. **Apply Settings** saves them; **Apply Settings + Launch** saves them and asks Steam to start the game, so Steamworks can initialize normally. Keep the launcher open while playing if you enabled the loading-screen warning. Your choices are remembered.

The FPS patch keeps scripted cutscenes and menus at 30 FPS. The optional loading warning requires the launcher to stay open while playing.

## Build

On Windows with .NET Framework 4.x, run `source/Build.cmd`. Keep the `runtime` and `assets` folders beside the built executable.

## Credits and licenses

- FPS patch based on [Spadira's WayOfTheSamuraiFPSUnlock](https://github.com/Spadira/WayOfTheSamuraiFPSUnlock). GPLv3 license, upstream readme, and local modifications are in `runtime/FPSPatch`.
- [DXVK](https://github.com/doitsujin/dxvk), distributed under its zlib license in `runtime/DXVK-LICENSE.txt`.
- [Steamless](https://github.com/atom0s/Steamless) is a separate tool; it is not included in this project.

This launcher project is licensed under GPL-3.0-only. See `LICENSE`. The package contains no game executable, game assets, or save files. Use it with your own copy of the game.
