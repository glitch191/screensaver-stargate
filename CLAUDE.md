# CLAUDE.md

Durable context for working on this repository. Current status and next steps are in `HANDOFF.md`.

## Project

Screensaver Stargate: a native Windows screensaver (`.scr`) inspired by the Star Gate sequence of *2001: A Space Odyssey*. Two walls of neon light stream out of a bright seam at the screen center, split by a thin black line, rendered in real time with OpenGL shaders and a film treatment (grain, gate weave, halation, softness).

- Owner: a single user, personal Windows machine, advanced technical level. The user writes in French; answer in French.
- Public repository: https://github.com/glitch191/screensaver-stargate (branch `main`, release `v1.0.0`).
- The original specification was a French prompt pasted at the start of the first session. It is not stored in the repository; its requirements are summarized below.

## Hard rules (from the specification, apply everywhere)

- **No em dash (U+2014) and no en dash (U+2013)** anywhere: UI, logs, README, docs, code comments, strings, commit messages, and also in chat replies. Use commas, colons, parentheses or a plain hyphen. Ranges: "10 to 20" or "10-20". Check before finishing: search the project (excluding `bin`, `obj`, `publish`, `dist`, `.git`) for `[\u2013\u2014]`.
- **No emoji and no AI-evoking icons** (sparkles, magic wand, robot, brain, crystal ball, `auto_awesome`, and the like) in the UI, docs, comments, commits or logs.
- **English** for the UI, all project files, comments and commit messages. Code identifiers in English.
- **Settings window**: sober standard Windows dialog look (Segoe UI, native controls, no gradients, no glass, no emoji, no promotional text), fluid layout (TableLayoutPanel), readable and without truncation at 100%, 125% and 150% scale, clickable targets at least 32 px high, concrete labels, error messages that say what to fix. The live preview is the only animation and stops when the window is minimized or closed. The full screen scene is exempt from these rules.
- **Scope**: no sound, no clock, no text or logo in the scene, no MSI installer, no auto update, no telemetry, no macOS or Linux. Do not add unrequested features. Features added later at the user's request: film look, scroll orientation.
- **Work only in this folder**; never write to system folders; build and tests must not need administrator rights.
- Keep code simple and readable, no premature abstraction.

## Stack and why

- **C# on .NET 10 (LTS)**, `net10.0-windows`. The specification said .NET 8, but .NET 8 support ends on 2026-11-10; the user chose .NET 10 (supported until 2028-11).
- **WinForms** for windows and the settings dialog.
- **OpenGL 3.3 core with GLSL shaders**, through **OpenTK.Graphics 4.9.4 used only for the GL bindings**. The GL context is created with WGL via P/Invoke (`src/GlContext.cs`), not GLFW: this avoids a native DLL and lets one code path render into a full screen form, the `/p` child window and the settings preview panel.
- **Self-contained single-file publish**, compressed (`EnableCompressionInSingleFile`). `dist\ScreensaverStargate.scr` is a renamed copy of the executable, about 48.0 MB. WinForms does not support trimming, hence the size.
- Per-Monitor V2 DPI awareness is set with `ApplicationHighDpiMode` in the project and `Application.SetHighDpiMode` at startup, not in `app.manifest` (a manifest DPI entry triggers warning WFO0003, and `build.ps1` treats warnings as errors).

## Architecture

```
src/Program.cs          Entry point; /s, /p, /c, windowed and screenshot modes
src/CommandLine.cs      Windows protocol (/s, /c[:hwnd], /p hwnd) and development options
src/Settings.cs         Settings POCO, JSON load/save (%APPDATA%\ScreensaverStargate\settings.json), Validate()
src/SettingsForm.cs     Settings dialog with live preview (GlPanel)
src/SceneForm.cs        Full screen (one per monitor) and windowed scene forms; input and exit handling
src/PreviewWindow.cs    /p child window (NativeWindow)
src/GlPanel.cs          Preview control for the settings dialog
src/RenderLoop.cs       One render thread per window: timing, pacing, diagnostics, screenshots
src/Simulation.cs       Fixed-step logic (240 Hz), interpolation, phase wrapping (Period = 256)
src/SceneRenderer.cs    GL resources and passes; FrameParams (settings to shader values)
src/GlContext.cs        WGL OpenGL 3.3 core context on an HWND, swap interval
src/Diagnostics.cs      Frame time stats and overlay text, allocation free
src/GlyphFont.cs        5x7 pixel font texture for the overlay
src/Native.cs           Win32 declarations (raw input, WGL, timers, PrintWindow)
src/Log.cs              %APPDATA%\ScreensaverStargate\log.txt, capped at 1 MB
src/Shaders/*.glsl      Embedded resources: fullscreen_vert, scene_frag, bloom_down_frag, bloom_up_frag, composite_frag
tests/TimingCheck/      Console check of frame rate independence (links src/Simulation.cs, Settings.cs, Log.cs)
tools/                  PowerShell capture, measurement and protocol test scripts
docs/screenshots/       Reference images used by the README
```

Rendering pipeline per frame:

1. **Scene** (`scene_frag.glsl`) into an R11G11B10F target: height-normalized coordinates; perspective depth = 1 / |x|, logarithmic beyond depth 6 so colors stay readable near the vanishing point. Large OKLCH color fields per wall (constant L 0.72 and C 0.21, so fades never go grey), continuous lines with streaks, pattern segments (dots, slanted hatching, bars) crossfaded along the depth, "ladder" bar columns, glowing patches, and a hot seam near the center. Lines are box filtered by their pixel footprint, so dense areas converge to their average instead of aliasing. The vertical orientation simply swaps the two axes (`q = q.yx`).
2. **Bloom**: dual Kawase down (bright pass on the first level) and up over up to 6 half-resolution levels, spread weight 0.6, threshold 0.3.
3. **Composite** (`composite_frag.glsl`): gate weave, lens softness and chromatic aberration, bloom with red halation, lamp flicker, hue-preserving highlight compression, vignette, sRGB, grain (24 film frames per second, sized by screen height / 1080). Then the **center line** (exactly at the window center, width = setting x height / 1080, applied in display values so the perceived width matches), then the diagnostics text. The line and the text are not affected by the film effects.

Threads and timing:

- Each window has its own render thread and WGL context, so every monitor follows its own vertical sync. The UI thread handles only messages and input.
- Motion comes from measured elapsed time fed to a fixed 240 Hz simulation step (frame delta capped at 250 ms), with interpolation for rendering. Nothing depends on a frame count or an assumed refresh rate. Film effect time is the simulation time wrapped at 4096 s.
- Pacing: vertical sync, or a high resolution waitable timer plus a short spin when a cap applies. With vsync on, a safety cap at 1.25x the refresh rate prevents spinning if the driver stops blocking (for example while displays sleep).
- No allocation in the frame loop; every GL object is released explicitly.

Screensaver behavior (`/s`): one borderless topmost form per screen (black forms on non-primary screens with "Primary screen only"), cursor hidden, single instance via the mutex `Local\ScreensaverStargate.Scene`. It exits on any key or mouse button or wheel through **Raw Input with RIDEV_INPUTSINK** (works without focus), or on a mouse move beyond 10 px x DPI/96 from the startup position. The exit cause is logged. If OpenGL 3.3 fails: `/c` shows a message and stays usable, `/s` exits, the cause is logged.

## Settings (defaults)

WallMode Perspective, Orientation Horizontal, Direction TowardViewer, Speed 50, LineDensity 50, LineThickness 40, ColorChangeSpeed 40, BloomIntensity 35, CenterLineWidth 6 (px at 1080p, range 1 to 40), CenterLineEdge Soft, Screens AllScreens, FrameRateLimit Automatic, CustomFrameRate 144 (20 to 1000), VerticalSync true, ShowDiagnostics false. A missing or unparsable file falls back to defaults; one invalid enum value makes the whole file fall back to defaults (not per field).

## Commands

The .NET 10 SDK is installed per user in `%LOCALAPPDATA%\Microsoft\dotnet` and is **not on the PATH** by default in new shells:

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
dotnet build -c Debug                       # quick build, bin\Debug\net10.0-windows\win-x64\
dotnet run --project tests\TimingCheck -c Release
.\build.ps1                                 # timing checks + publish + dist\ScreensaverStargate.scr (warnings are errors)
.\build.ps1 -SkipChecks
```

Git for Windows is at `C:\Program Files\Git\cmd\git.exe` (it may not be on the PowerShell PATH; Git Bash works). `gh` is installed at `C:\Program Files\GitHub CLI\gh`.

Development options: `--windowed`, `--size WxH`, `--screenshot file.png` (one offscreen frame after exactly 3 s of simulated time; with `/c`, a capture of the settings window), `--seed n`, `--diag`, `--mode perspective|flat`, `--orientation horizontal|vertical`, `--ui-scale 125`.

Test scripts (see README for details): `tools\capture-scenes.ps1`, `tools\measure-center-line.ps1` (`-Horizontal` for the vertical orientation), `tools\capture-window.ps1`, `tools\test-screensaver.ps1`, `tools\test-preview.ps1`, `tools\test-settings.ps1`, `tools\test-settings-ui.ps1`, `tools\memory-test.ps1`. Local outputs go to `screenshots\raw\` (git ignored).

## Pitfalls learned

- **A `.scr` started through the shell (Start-Process, double click) always runs `/S`** and ignores the arguments. Test with `publish\ScreensaverStargate.exe` or a `bin\` executable.
- **Never capture the user's screen with CopyFromScreen**: it once recorded the user's Firefox window. Use `PrintWindow` with `PW_RENDERFULLCONTENT` (2) on the program's own window.
- In full screen the window uses independent flip: screen captures and PrintWindow return a **stale frame** (it looked frozen and without the overlay text). Measure full screen through the log lines written at exit ("Render loop ended", "Diagnostics, last 2 s").
- The full screen forms have `ShowInTaskbar = false`, so `Process.MainWindowHandle` is 0; find the window with `WindowFromPoint`.
- A window launched from a background shell may not get the focus: that is why input uses Raw Input. Synthetic clicks need a short delay between down and up.
- **The user may be using the machine during tests**: a real mouse move closes the screensaver and looks like a failure. Rerun before debugging; the log states the exit cause.
- Tool calls time out after 10 minutes: run long tests (memory) in a detached process (`Start-Process powershell ...`) and stop them by PID, never by process name.
- The sandbox blocks some `Remove-Item` commands (with variables or chained with other commands): use a literal path in a separate command.
- In PowerShell, backticks inside double-quoted replacement strings corrupt Markdown; `.Replace()` fails silently when the pattern is missing. Prefer the Edit tool, or throw when the pattern is not found.
- WinForms: an AutoSize GroupBox with a `Dock = Fill` child collapses; with a `Dock = Top` child it no longer sizes. Working pattern: grids `Fill` inside groups, groups `Top` inside the column panels.
- WinForms analyzers: public properties on controls need `[Browsable(false), DesignerSerializationVisibility(Hidden)]` (WFO1000). `Buffer` is ambiguous with OpenTK (`System.Buffer`).
- On this machine, **"Unlimited" with vsync off stays at 360 FPS**: the driver reports swap interval 0, so the cap comes from the compositor or an NVIDIA setting, not from the code.
- The center line measurement is biased by steep glow gradients (about 0.5 px in the vertical perspective mode); the line itself is computed analytically at the exact center.

## Development machine (for reference)

Windows 11, one 3440x1440 screen at 360 Hz, 100% scale, NVIDIA GeForce RTX 5070 Ti (an AMD Radeon integrated GPU is also present but was never used for tests).
