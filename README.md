<p align="center"><img src="assets/logo/padfin-logo.png" width="160" alt="Padfin logo"></p>

# Padfin

A controller-first [Jellyfin](https://jellyfin.org) client built with Godot 4.7 and C#, for Steam on
Windows, Linux and Steam Deck. Xbox and PlayStation are planned after the PC release.

Padfin is an independent project. It is not affiliated with or endorsed by Jellyfin.

It shares its interface with [RomBox](https://github.com/HowDoDownhill/rombox) through the FishUI
library in `addons/fishui`: the Glass and Simple styles, colour palettes, animated backgrounds,
frosted panels, gliding selection highlight, controller glyphs and interface sounds.

## What works

- **Sign in without typing.** Servers on your network are found automatically, and Quick Connect
  signs you in with a six-digit code. An on-screen keyboard covers the rest.
- **Sidebar** between the top and bottom bars, with pages for Home, TV, Movies, Collections,
  Requests and Settings. The top bar shows your server's icon and name.
- **Home**: Continue Watching, Next Up, your libraries, and the latest additions to each. The row
  you are on always sits in the same place, title included.
- **TV, Movies, Collections**: poster grids across all your libraries that load as you scroll, with sorting.
- **Search** with live results in two sections: On Server for your library, and On Seerr for
  titles you could request.
- **Seerr**: request a movie, a whole show or one season, in 4K where the server offers it, and
  follow it on the Requests page. Accounts with the rights can choose the server, quality profile
  and folder, and approve or decline other people's requests. Connect from the Requests page with
  a Jellyfin account, a Seerr account or an API key.
- **Genre jump**: on TV and Movies, hold LB or RB for a grid of genres that filters the page.
- **Settings** in nine sections:
  - Playback: streaming quality, hardware decoding, buffer size, next-episode autoplay, skip lengths, rewind on
    resume, what to do at intros, credits and recaps, playback info.
  - Video: picture shape, scaling quality, HDR tone mapping, deinterlace, banding.
  - Audio: preferred language, remembered track, speakers, night mode, delay, passthrough.
  - Subtitles: preferred language, mode, remembered track, size, position, colour, background,
    restyling, delay.
  - Home: which rows show, how long Next Up remembers a show, hiding watched items.
  - Library: default sort, card size, titles under cards, backdrops.
  - Appearance: style, theme, background, interface size, clock, sounds, fullscreen.
  - Account: server, sign out, Quick Connect for another device, Seerr.
  - System: artwork cache, version, exit.

  Language, subtitle and autoplay preferences are saved to your Jellyfin account, so they follow
  you to other clients.
- **Details**: movies, series with seasons and episodes, watched and favourite toggles.
- **Playback** through libmpv, drawn on the GPU with hardware decoding. Nearly every file direct
  plays, and one that fails is retried as a stream the server converts.
  - Starts on the audio and subtitle tracks your Jellyfin preferences pick, with menus to change
    them. Your choice is reported back, so the server can remember it per show.
  - Scrubbing shows a thumbnail and time before it seeks, where the server has trickplay images.
  - Chapter marks on the bar and a chapter list.
  - Skip intro, credits and recaps on servers with media segments, an Up Next card in an episode's
    last seconds, and automatic next episode.
  - Quality and speed can be changed while playing. Titles with several versions offer a choice.
  - Resume, progress and pause reporting, and a clean stop when the window is closed mid-video.

## Controls

| Action | Controller | Keyboard |
|---|---|---|
| Move | D-pad or left stick | Arrow keys |
| Select | A | Enter |
| Back | B | Escape |
| Details (home), Sort (library), Delete (keyboard) | X | F2 |
| Search | Y | F3 |
| Previous or next sidebar page | LB or RB | Page Up or Page Down |
| Genres | Hold LB or RB | Hold Page Up or Page Down |
| Move to the sidebar | B, or Left from the first column | Escape |
| Menu | Start | F1 |
| Fullscreen | | F11 |

During playback: **A** pauses, **Left/Right** scrub 10 seconds back or 30 forward and **LB/RB**
scrub 5 minutes (the lengths are settings). A scrub seeks after a short pause or on **A**, and **B**
cancels it. **X** picks audio, **Y** picks subtitles, **Start** opens chapters, quality, speed and
playback info, **B** stops.

## Install

You need a 64-bit PC running Windows 10 or 11, or Linux.

1. Download `padfin-windows.zip` or `padfin-linux.zip` from
   [Releases](https://github.com/HowDoDownhill/padfin/releases).
2. Extract it to any folder you can write to.
3. Run `padfin.exe` on Windows or `padfin.sh` on Linux.

The Linux download carries its own video player: `libmpv.so.2` sits beside the executable with
FFmpeg and the codecs built in, so there is no mpv or FFmpeg to install. It needs glibc 2.35 or
newer, which covers Ubuntu 22.04, Debian 12, Fedora, Arch and SteamOS 3.5 onward. From the system it
uses the libraries a desktop already has: OpenGL, fontconfig, FreeType, HarfBuzz, FriBidi, GnuTLS,
ALSA, PulseAudio (or PipeWire's stand-in for it) and libva for hardware decoding.

The Linux build has been run on a test machine with software graphics. It has not yet been tried on
a Steam Deck or with hardware decoding.

## Updates

Padfin checks GitHub for a newer version when it starts and offers to install it. It downloads the
build, swaps it in and restarts. **Settings > System > Padfin** checks at any time.

**Settings > System > Test builds** chooses whether early builds are offered. It is on by default
while Padfin has only test builds. If an update goes wrong, `update.log` in Padfin's data folder
says why.

## Steam

Padfin is not on the Steam store. It adds itself to your Steam library as a non-Steam game. On
Windows and Linux, when Padfin starts outside Steam and finds Steam installed, it offers to do this
and restart from Steam. That gets you Steam Input for any controller, Big Picture and Steam Deck
Game Mode, and the Steam overlay.

Steam only reads new library entries when it starts, so Padfin closes Steam if it is open, adds its
shortcut with library artwork and Steam Input turned on, and starts itself again through Steam.
**Not Now** asks again next time, and **Don't Ask Again** stops the offer.
**Settings > System > Steam library** does the same at any time. If you move the Padfin folder, the
offer comes back to fix the shortcut.

`padfin -- --add-to-steam` writes the shortcut and exits, for scripts. Close Steam first.

## Building

You need Godot 4.7.2 (.NET build), the .NET 8 SDK, and libmpv.

1. **libmpv.** On Windows run `tools/fetch-mpv.ps1`, which downloads `libmpv-2.dll` into
   `native/win-x64` (needs 7-Zip). On Linux install your distribution's `libmpv2` package to run from source. The
   Linux download ships a libmpv that the `Linux libmpv` workflow in `.github/workflows` builds
   from the mpv, FFmpeg, libass, libplacebo, dav1d and libdrm sources.
2. **FishUI.** The interface library is a git submodule. Fetch it with
   `git submodule update --init`, or clone with `--recurse-submodules`.
3. Open the project in Godot, or run it from a terminal:

   ```
   dotnet build
   godot --path .
   ```

To make a Windows build, run `.\build.bat`. It exports to `build\windows` and puts
`libmpv-2.dll` beside the executable, where an exported build needs it. `-Output <folder>` builds
somewhere else, and `-Release` also packs `releases\padfin-windows.zip`, the file a GitHub release
needs for the updater.

### Developer arguments

Pass these after `--` on the Godot command line.

| Argument | Effect |
|---|---|
| `--windowed` | Start in a window. |
| `--config <file>` | Use this settings file in place of `padfin.cfg`, leaving the real one untouched. |
| `--play <file or url>` | Open the player on a file, with no server. |
| `--keys ui_down,hold:fish_rb,joy:0,quit` | Press input actions in order. `hold:` holds one down. `joy:N` presses controller button N. `quit` exits as closing the window does. |
| `--shot <png> [seconds]` | Save a screenshot after a delay, then exit. |

## Layout

| Path | Contents |
|---|---|
| `scripts/App.cs` | Padfin's pages, saved settings, the server in the top bar, startup |
| `scripts/autoloads` | Settings, the Jellyfin and Seerr API clients, the artwork cache |
| `scripts/seerr` | Seerr data types and the connect flow |
| `scripts/ui/screens` | Connect, Login, Home, Library, Details, Search, Requests, Seerr details, Settings, Player |
| `scripts/ui/widgets` | The card and card row for Jellyfin and Seerr items |
| `scripts/player` | libmpv bindings and the OpenGL bridge that lets mpv draw into a Godot texture |
| `addons/fishui` | FishUI, the interface library shared with RomBox: the app shell, screens and overlays, widgets, the look, and the Steam shortcut writer. A git submodule. |
| `assets/steam` | Artwork written into the Steam library entry |
| `addons/controller_icons` | Controller glyphs (C# port of rsubtil's Controller Icons) |

Settings, your Jellyfin token and your Seerr sign-in are stored in Godot's user data folder as
`padfin.cfg`. They are in plain text, so keep that file private.

## Licence

FishUI is GPL-3.0, and the libmpv builds Padfin ships for Windows and Linux are GPL as well, so
Padfin is distributed under the GPL-3.0.
