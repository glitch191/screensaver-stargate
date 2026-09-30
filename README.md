# Screensaver Stargate

A native Windows screensaver inspired by the Star Gate sequence at the end of *2001: A Space Odyssey*: two walls of neon light streaming out of a bright seam in the middle of the screen, split by a thin black line. The walls stand left and right (horizontal scrolling, vertical center line) or above and below (vertical scrolling, horizontal center line). Everything is drawn in real time by OpenGL shaders, with a film treatment (grain, gate weave, halation, softness) so the picture feels like projected film rather than clean vector graphics.

![Perspective mode, horizontal orientation, rendered at 1920 x 1080 by the current version](docs/screenshots/hero-perspective-1920x1080.jpg)

Detail at 100% (one image pixel per screen pixel), showing the film grain, the softness, the red halation and the slightly irregular lines:

![Film treatment detail at 1:1](docs/screenshots/detail-film-1x.png)

Vertical orientation, walls above and below:

![Perspective mode, vertical orientation](docs/screenshots/hero-perspective-vertical-1920x1080.jpg)

## Requirements

- Windows 10 or 11, 64-bit.
- A graphics driver with OpenGL 3.3 (any GPU from the last ten years, integrated ones included).
- To build: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). No administrator rights are needed; a per-user install works (`dotnet-install.ps1 -Channel 10.0`, which puts it in `%LOCALAPPDATA%\Microsoft\dotnet`). `build.ps1` finds it there or on the `PATH`.

## Build

```powershell
.\build.ps1
```

This runs the timing checks, publishes a self-contained single-file executable and copies it to `dist\ScreensaverStargate.scr`. Warnings are treated as errors. Use `.\build.ps1 -SkipChecks` to skip the timing checks.

Size of `dist\ScreensaverStargate.scr`: **48.0 MB** (self-contained .NET 10 runtime and WinForms, compressed; no installation of .NET is needed on the target machine).

## Install

- Right-click `dist\ScreensaverStargate.scr` and choose **Install**. Windows opens the screen saver settings with it selected.
- Or copy the file to `C:\Windows\System32` (needs administrator rights) and pick it in the screen saver settings.

The program never writes to system folders. Its only files are in `%APPDATA%\ScreensaverStargate`: `settings.json` and a small `log.txt` (capped at 1 MB).

To uninstall, choose another screen saver and delete the `.scr` file and `%APPDATA%\ScreensaverStargate`.

## Settings

Open them from the Windows screen saver dialog (**Settings...**), by right-clicking the `.scr` file and choosing **Configure**, or by running it without arguments. The window shows a live preview; every change is visible immediately and saved with **OK**.

| Setting | Effect |
| --- | --- |
| Wall mode | **Perspective**: two walls converging to a vanishing point. **Flat**: the same walls seen straight on, scrolling sideways. |
| Orientation | **Horizontal**: walls on the left and right, light streaming sideways, vertical center line. **Vertical**: walls above and below, light streaming up and down, horizontal center line. |
| Direction | **Toward viewer**: light streams from the center line to the edges. **Away from viewer**: the reverse. |
| Speed | Scroll speed, 0 to 100. |
| Line density | Number of lines on each wall. |
| Line thickness | Width of the lines relative to their spacing. |
| Color change speed | How fast the colors fade into new neon colors (0 keeps them fixed along the walls). |
| Bloom intensity | Glow around the lights, from 0 (off) to strong. The default is moderate. |
| Center line width | Width of the black center line in pixels on a 1080-pixel-high screen; it scales with the screen height. |
| Center line edge | **Soft** or **Sharp** edges. |
| Screens | **All screens**, or **Primary screen only** (other screens stay black). |
| Frame rate limit | **Automatic** (the refresh rate of each screen), **Custom value** (20 to 1000), or **Unlimited**. |
| Vertical sync | Synchronizes each frame with the refresh of its screen. Recommended. |
| Show diagnostics | Small overlay with the detected refresh rate, the average frame time and the worst 1% of frame times. |
| Reset to defaults | Restores every setting (saved only with OK). |

A missing or damaged `settings.json` is ignored and the defaults are used; the reason is written to `log.txt`.

## Screensaver protocol

| Argument | Behavior |
| --- | --- |
| `/s` | Full screen on the configured screens. One borderless window per screen, each with its own random seed, GL context, render thread and vertical sync. The cursor is hidden. Any key, any click, the mouse wheel or a mouse move of more than 10 pixels (scaled with DPI) closes it; keys and clicks are caught through raw input, so this works even if another window keeps the focus. A second `/s` instance exits immediately. |
| `/c`, `/c:<hwnd>` | Settings window, owned by the given window when there is one. |
| `/p <hwnd>` | Preview drawn in a child window of the given parent (the small monitor in the Windows dialog), at its small native size, ignoring input, and exiting when the parent is closed. |
| no argument | Same as `/c`. |

The program is Per-Monitor V2 DPI aware and renders at the native resolution of each screen. If OpenGL 3.3 is not available, `/c` shows a clear message (the settings stay usable), `/s` exits quietly, and the cause is logged.

## Development options

These are not part of the Windows protocol.

| Option | Effect |
| --- | --- |
| `--windowed` | Scene in a normal, resizable window. |
| `--size WxH` | Window size, or screenshot size, in pixels. |
| `--screenshot file.png` | Renders one frame after exactly 3 seconds of simulated time at `--size` (default 1920x1080), saves it and exits. With `/c`, saves a capture of the settings window instead. |
| `--seed n` | Fixed random seed, for reproducible pictures. |
| `--diag` | Diagnostics overlay (also available as a setting). In full screen the overlay cannot be captured by screen capture tools (the window is presented directly), so the last values are also written to `log.txt` when the screensaver closes. |
| `--mode perspective\|flat` | Overrides the wall mode for this run. |
| `--orientation horizontal\|vertical` | Overrides the scroll orientation for this run. |
| `--ui-scale 125` | Emulates a display scale for the settings window (fonts and layout), to check 125% and 150% on a 100% screen. |

Starting a `.scr` through the Windows shell always runs `/S`, so the test scripts use `publish\ScreensaverStargate.exe`, which is the same binary.

Scripts in `tools\`:

- `capture-scenes.ps1`: screenshots of both modes at 1920x1080, 2560x1440, 2560x1080 and 3440x1440 (`-Orientation vertical` for the other orientation).
- `measure-center-line.ps1`: measures the center line position and width in screenshots, to sub-pixel precision (`-Horizontal` for the vertical orientation).
- `capture-window.ps1`: captures the program's own window (never the rest of the desktop).
- `test-screensaver.ps1`: `/s` checks (single instance, key, click, mouse threshold). Covers the screen for a few seconds per step.
- `test-preview.ps1`: `/p` check with a test parent window.
- `test-settings.ps1`: damaged settings file, invalid values and frame rate options. Backs up and restores `settings.json`.
- `test-settings-ui.ps1`: drives the settings window with UI Automation (selects options, checks that the preview changes, OK, reopens, checks that they were saved and loaded).
- `memory-test.ps1`: long run with memory samples every minute.

## How it works

- **Rendering**: one fullscreen triangle per pass. The scene shader computes, for every pixel, where it lands on the walls (in perspective, depth = 1 / distance from the center) and evaluates procedural patterns there: continuous lines with light streaks, grids of dots, slanted hatching, rows of bars and columns of short bars, in large color fields. Colors use OKLCH at constant lightness and chroma, so fades between colors never go through grey. Lines are box filtered by their pixel footprint, which keeps the dense area near the vanishing point smooth and lets it pile up into a bright seam, as in the film.
- **Bloom**: bright pass and dual Kawase blur over up to six half-resolution levels (R11G11B10 float), added back with a warm halation component.
- **Film treatment**: gate weave (about one pixel of drift), slight lens softness and chromatic aberration, lamp flicker and luminance grain (no random dust or specks), all changing at 24 film frames per second and sized relative to the screen height.
- **Center line**: drawn last, over the walls, the glow and the film effects, from the exact window center, with width proportional to the height.
- **Orientation**: the vertical orientation swaps the two screen axes in the scene shader and draws the center line horizontally; everything else is shared.
- **Aspect ratio**: all coordinates are normalized by the window height, so a 21:9 screen shows a wider field (more of each wall) instead of a stretched picture.
- **Timing**: motion comes from a fixed 240 Hz simulation step fed with the measured frame time (capped at 250 ms), and the renderer interpolates between the last two steps. Nothing depends on a frame count or an assumed refresh rate. Display pacing comes from vertical sync, or from a high resolution waitable timer when a frame rate cap applies. With vertical sync on, a safety cap at 1.25 times the refresh rate stops the loop from spinning if the driver stops blocking (for example while the displays sleep); it never engages while vertical sync works.
- **Threads**: every window has its own render thread and WGL context, so each screen follows its own vertical sync. The UI thread only handles input and window messages.
- **Memory**: no allocation in the frame loop (buffers are reused, the diagnostics text is built into a fixed array), and every GL object is released explicitly.

## Verification

Measured on this development machine: Windows 11, one 3440x1440 screen at 360 Hz, NVIDIA GeForce RTX 5070 Ti, 100% display scale.

- **Timing checks** (`tests\TimingCheck`, run by `build.ps1`): the same 30 s of wall-clock time gives the same scroll and color phase at 60, 59.94, 144, 240 and 360 Hz, with random jitter and with 100 ms hitches (difference: one interpolation step, constant). A 10 s stall moves the scene by at most the 250 ms cap. Wrapping of the pattern phases is continuous.
- **Frame times** (diagnostics mode): full screen 3440x1440 at 360 Hz with vertical sync, average 2.80 ms, worst 1% 2.95 ms. Frame rate caps: custom 144 gives 6.94 ms, custom 60 gives 16.67 ms. With vertical sync off and Unlimited, the driver confirms swap interval 0 but frames stay paced at 360 Hz on this machine, which points to the Windows compositor or a driver setting (for example forced vertical sync or a frame limiter in the NVIDIA control panel) rather than the program.
- **CPU**: one render thread uses about 41% of one core at 360 FPS (the driver waits actively on each swap), 24% at 144 FPS and 6% at 60 FPS. Use a custom frame rate to save power.
- **Memory**: see the table below.
- **Screenshots**: both modes and both orientations at 1920x1080, 2560x1440, 2560x1080 and 3440x1440 are in `docs\screenshots`. Center line measured with `measure-center-line.ps1`: in the horizontal orientation, centered within 0.15 pixel at every size, width 6.3 px at 1080 and 8.3 px at 1440 for a 6 px setting (targets 6 and 8; the small excess comes from the soft edge and the glow gradient), Sharp edge within 0.12 pixel. In the vertical orientation, flat mode is centered within 0.26 pixel with the same widths; in perspective the measurement reads 0.4 to 0.6 pixel off, which is the bias of the method on the steep and asymmetric glow of the horizon (the line itself is computed at exactly half the height).
- **Settings window**: checked at 100%, 125% and 150% with `--ui-scale` (no truncation or overlap). The real Windows scaling was not changed during development; please confirm on a screen set to 125% or 150%.
- **Protocol**: `/s` (single instance, key, click, mouse threshold with a 3 px move ignored), `/p` in a test parent window (child created, exits when the parent closes), `/c` and no argument.
- **Settings persistence**: `test-settings-ui.ps1` selects Flat and Vertical, sees the preview change, clicks OK, and finds both saved in `settings.json` and selected again when the window reopens.
- **Not verified here**: several physical screens (only one was available; the code creates one window, seed, context and render thread per screen), a real 125% or 150% Windows scale, and an integrated GPU at 3440x1440.

### Memory over 30 minutes

`tools\memory-test.ps1`, scene in a 1920x1080 window with diagnostics at 360 FPS (vertical sync), sampled every minute. Private bytes, working set, handles and threads of the process; the managed allocation figures come from the diagnostics log line written every minute.

| Minute | Private (MB) | Working set (MB) | Handles | Threads |
| --- | --- | --- | --- | --- |
| 0 | 173.9 | 169.1 | 478 | 19 |
| 3 | 151.0 | 166.4 | 472 | 12 |
| 5 | 151.8 | 166.7 | 472 | 13 |
| 10 | 152.5 | 167.4 | 472 | 13 |
| 15 | 151.6 | 167.1 | 468 | 13 |
| 20 | 151.9 | 167.2 | 468 | 12 |
| 25 | 151.1 | 166.5 | 468 | 12 |
| 29 | 151.6 | 166.8 | 468 | 12 |
| 30 | 155.2 | 170.7 | 468 | 12 |

After the start-up settles (minute 3), private memory stays between 150.7 and 155.2 MB with no upward trend, and handle and thread counts stay flat. The managed heap went from 1011 KB to 1190 KB in 29 minutes, which is the once-a-minute memory log itself (about 6 KB per minute), with zero garbage collections: nothing is allocated per frame over roughly 600,000 frames. This 30-minute run used the build just before the Orientation setting was added; a second 15-minute run of the final code (1600x900 window) showed the same pattern (private 100.8 to 103.3 MB, managed heap 1073 KB to 1160 KB, zero collections).

## Project structure

```
ScreensaverStargate.csproj   Project (net10.0-windows, WinForms, OpenTK.Graphics 4.9.4 for GL bindings)
app.manifest                 Windows 10/11 compatibility, asInvoker
build.ps1                    Build and produce dist\ScreensaverStargate.scr
src\Program.cs               Entry point, screensaver modes
src\CommandLine.cs           /s /c /p and development options
src\Settings.cs              Settings, JSON load and save with validation
src\SettingsForm.cs          Settings window with live preview
src\SceneForm.cs             Full screen and windowed scene windows, input handling
src\PreviewWindow.cs         /p child window
src\GlPanel.cs               Preview control used in the settings window
src\RenderLoop.cs            Render thread: timing, pacing, screenshots
src\Simulation.cs            Fixed time step logic with interpolation
src\SceneRenderer.cs         GL resources and passes (scene, bloom, composite)
src\GlContext.cs             WGL OpenGL 3.3 core context on a window handle
src\Diagnostics.cs           Frame time statistics and overlay text
src\GlyphFont.cs             5x7 pixel font for the overlay
src\Native.cs                Win32 declarations
src\Log.cs                   Local log file
src\Shaders\*.glsl           Scene, bloom and composite shaders (embedded)
tests\TimingCheck\           Frame rate independence checks (console, no packages)
tools\                       Capture, measurement and protocol test scripts
docs\screenshots\            Reference captures
```

## License and credits

Personal project. The look is inspired by Douglas Trumbull's slit-scan Star Gate in *2001: A Space Odyssey* (1968); no footage or image from the film is used, everything is generated by the shaders.
