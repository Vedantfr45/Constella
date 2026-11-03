# Constella

Synced lyrics for whatever is playing on your PC, drawn as constellations. Every lyric line
becomes a constellation and every word a star: as a word is sung its star fades in, a thin line
draws itself from the previous star, and the word glows large beside it before settling into a
small label. A soft light runs around the screen edges. There are more styles too (see
*Lyric styles* below), and a phone-style lock screen to show the lyrics off.

Works with Spotify, YouTube / YouTube Music in a browser, Apple Music, and any app that shows up
in Windows' media controls (the "now playing" card in the volume flyout).

Built by Vedant.

## Download

Get `Constella-win-x64.zip` from the [latest release](https://github.com/Vedantfr45/Constella/releases/latest),
unzip it and double-click `Constella.exe`. Nothing else needs installing. The website lives in
`docs/` and is served by GitHub Pages.

## Run it from the source

1. Open `C:\Work\Coollyrics\Constella` in File Explorer.
2. Double-click **`Constella.bat`**.
   - The first time, if the .NET 8 SDK isn't installed, it installs it with `winget` (say Yes
     if Windows asks). This happens only once.
   - It then builds the app (about 20 seconds) and starts it.
3. The settings window opens and a star icon appears in the system tray (bottom-right; click the
   `^` arrow if you don't see it).
4. Play a song in Spotify or YouTube Music. The lyrics appear after a moment.

After the first build you can start it straight from `app\Constella.exe`.

| Action | How |
|---|---|
| Show / hide the lyrics | **Ctrl + Alt + L** (change it in settings) |
| Lock screen on / off | **Ctrl + Alt + K**, the tray menu, or Settings → *Show lock screen* |
| Lyrics 0.1 s earlier / later for the app that's playing | **Ctrl + Alt + [** / **Ctrl + Alt + ]** |
| Settings | Left-click the tray star (click again to close it) |
| Close / minimise settings | **Esc**, the *Close* button, or the X and – at the top right (Constella keeps running in the tray) |
| Load your own `.lrc` file, quit | Right-click the tray star |
| Lyrics a bit early or late for every song | Settings → *Lyrics timing* |

Ctrl+Alt+arrow keys are deliberately not used: some Intel graphics drivers rotate the screen
with them.

The overlay is click-through, so you can keep using your PC underneath it. When music is paused
for a few seconds the overlay fades away and stops drawing completely.

With the *Dim* or *Black* backdrop the overlay covers the desktop. Each time it appears it shows
a few-second reminder that the show/hide shortcut (Ctrl+Alt+L) hides it. *Hide lyrics* in the tray menu works
too, and while the overlay covers the desktop the settings window stays above it (the rest of
the time it's an ordinary window that other apps can cover).

Lyrics always stay inside a safe area (6% in from every edge, clear of the edge glow). Long words
and lines are set smaller or wrapped instead of running off the screen, at any text size.

## Lyric styles

| Style | What it does |
|---|---|
| Constellation | Each line is a constellation and each word a star, joined by lines as it is sung. |
| Stack | Big bold words pile up in the middle, newest at the bottom; the word being sung sits in a rounded box in the lyric colour with dark letters. |
| Spotlight | One huge word at a time, punching in as it is sung. |
| Karaoke | The line fills with colour from left to right as it is sung; the next line waits underneath. |
| Light wipe | Light sweeps through a dim line, word by word. |
| Type-on | Each word is typed out letter by letter, with a caret. |
| Underline | A glowing bar glides under the words as they are sung. |

## Edge light

Light around all four screen edges, in three motions:

- **Run**: a snake of light chases clockwise around the screen border, the same as Rimic's: a
  rounded head and a tail fading out over 30% of the border, cream-to-amber (or your colour,
  deepening inward), and nothing lit anywhere else. It's drawn per pixel into four thin strip
  bitmaps at 30 fps (`src/Overlay/SnakeRim.cs`). Speed 5 is one lap in 8 seconds; intensity is its brightness.
- **Breathe**: the whole edge brightens and dims with the music's loudness.
- **Still**: a steady glow.

The colour can follow the lyrics, or be red, white or any colour you pick.

## Lock screen

**Ctrl + Alt + K** (or the tray menu, or Settings) opens a full-screen, phone-style lock screen:
the date and a big clock at the top, the lyrics in the middle, a now-playing card (cover, title,
artist, progress) below, and the edge light running around it, on a black wallpaper with a
faint glow of the song's colour. Lyrics are the same size as on the normal overlay.
It's only a display. It never asks for a password, and any key or click closes it (input is ignored for
the first 0.8 s so the shortcut itself doesn't close it).

## Timing

Lyrics start fading in 0.15 s before each word, so they land on the beat. If an app's lyrics
still come in late or early, press **Ctrl + Alt + [** (earlier) or **Ctrl + Alt + ]** (later) while
it plays; a small message shows the new value. The nudge is saved per app, so Chrome and Spotify
each keep their own (`%LOCALAPPDATA%\Constellapp-timing.json`). Settings shows them and can reset them.
The *Lyrics timing* slider shifts every song from every app.

## Settings

- **Look**: style, font (Sans, Rounded, Serif, Grotesk, Mono), text size, lyric brightness
  (30–100%, for the words and stars; the edge glow has its own intensity).
- **Colour**: lyric, highlight and secondary colours, or *Match album art* (black, white and grey
  covers get white lyrics on a soft silver-grey).
- **Screen**: which monitor, backdrop (Clear, Dim, Black).
- **Edge light**: on/off, Run / Breathe / Still, colour, intensity, run speed.
- **Timing & keys**: lyrics offset, per-app nudges, show/hide shortcut, start with Windows, load a `.lrc` file.
- **Lock screen**: open it, 12- or 24-hour clock.

The window fits on the screen above the taskbar; if it ever doesn't, the mouse wheel scrolls it.
Settings are saved in `%APPDATA%\Constella\settings.json`.

## How it works

| Part | File |
|---|---|
| Reads the playing song, play/pause and position from Windows' media sessions | `src/Media/MediaWatcher.cs` |
| Smooth song clock, re-synced about twice a second | `src/Media/PlaybackClock.cs` |
| Finds synced lyrics on [LRCLIB](https://lrclib.net) (free, no key) and caches them | `src/Lyrics/` |
| LRC parsing; per-word stamps when present, otherwise words are spread by length | `src/Lyrics/LrcParser.cs` |
| The constellation animation | `src/Overlay/Constellation/` |
| The line styles (Stack, Spotlight, Karaoke, Light wipe, Type-on, Underline) | `src/Overlay/Lines/` |
| Edge glow, background stars, title card, toast | `src/Overlay/` |
| Lock screen | `src/LockScreen/` |
| Per-app timing nudges | `src/Lyrics/TimingMemory.cs` |
| System audio loudness (WASAPI loopback, nothing is recorded) | `src/Audio/LoudnessMeter.cs` |
| Settings window and tray menu | `src/Ui/` |

New lyric styles plug in through `ILyricStyle` (`src/Overlay/ILyricStyle.cs`) and appear in
settings automatically.

Lyrics are cached in `%LOCALAPPDATA%\Constella\lyrics`, and a log is kept at
`%LOCALAPPDATA%\Constella\constella.log` if something needs debugging.

## Build by hand

```powershell
cd C:\Work\Coollyrics\Constella
dotnet publish src\Constella.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o app
.\app\Constella.exe
```

Requires Windows 10 (2004) or later and the .NET 8 SDK.

To make the download for a GitHub release, double-click **`Make release.bat`**. It builds one
self-contained `Constella.exe` (no .NET needed on the other PC) and zips it to
`release\Constella-win-x64.zip`.

## Credits

Lyrics from LRCLIB. Bundled fonts (Nunito, Cormorant Garamond, Space Grotesk, JetBrains Mono)
are under the SIL Open Font License, see `src/Assets/Fonts/FONTS-LICENSE.txt`.
