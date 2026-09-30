# HANDOFF.md

State of the project at the end of the first working session (2026-09-29 and 2026-09-30). Durable context, conventions and pitfalls are in `CLAUDE.md`.

## Done

- **Version 1.0.0 is published.** Public repository https://github.com/glitch191/screensaver-stargate, local `main` in sync with `origin/main` at `c15bb85`, tag `v1.0.0` on that commit. The GitHub release "Screensaver Stargate 1.0.0" has the asset `ScreensaverStargate.scr` (50,347,596 bytes, SHA-256 `C2C90C4E6F801DF8260223B7DCA035DB3E82922BCDB781863FCE93A3DA81BE6F`, identical to the local `dist\` file). The user uploaded it by hand.
- All features of the specification: `/s`, `/c`, `/p`, no argument; perspective and flat modes; OKLCH color fades; bloom; center line (Soft or Sharp, width proportional to height); multi-monitor code; settings window with live preview and persistence; frame rate limit (Automatic, Custom, Unlimited) and vertical sync; diagnostics mode; `build.ps1`; README.
- Additions requested by the user during the session: film-like Star Gate look (reference images from the film), film treatment (grain, gate weave, halation, softness, flicker, chromatic aberration), scroll orientation (Horizontal or Vertical). The random dust specks were added, then **removed at the user's request** (keep the grain, no random artifacts).
- Verification performed (details and figures in README "Verification"): timing checks at 60, 59.94, 144, 240, 360 Hz with jitter and hitches; full screen 3440x1440 at 360 Hz, 2.80 ms average; frame caps 144 and 60; center line measurements in both orientations at 4 sizes; `/s`, `/p`, settings persistence through UI Automation; damaged settings file; 30 minute memory run (stable, no per-frame allocation, zero GC).

## In progress

Nothing is being edited: the working tree is clean.

The last open proposal in the conversation was not answered: add a GitHub Actions workflow (`.github/workflows/release.yml`, does not exist yet) that builds the `.scr` on `windows-latest` and attaches it to releases. It was proposed with triggers on tag push, on release published, and on manual run for an existing tag. Waiting for the user's decision.

## Next steps (by priority)

1. **Ask the user about the release workflow** above. If yes: `actions/setup-dotnet` with `10.0.x`, run `./build.ps1`, attach `dist/ScreensaverStargate.scr` (for example with `softprops/action-gh-release`). The timing checks run fine on a GitHub runner; GL tests cannot (no GPU). A CI build will not be byte-identical to the local one, so the SHA-256 in the v1.0.0 release notes would no longer match if the asset were replaced.
2. **User validation still needed on real conditions**, not possible on the development machine: real Windows scale at 125% and 150% (only emulated with `--ui-scale`), several physical monitors, an integrated GPU at 3440x1440, and the look itself over a long viewing.
3. **Visual reference**: the user suggested footage from https://www.youtube.com/watch?v=rn7MmS3vazU (5:40 to 6:35). YouTube blocked access with a sign-in bot check, so it was never seen. The look is based on the four still images the user shared in chat (not saved in the repository). If the user provides frames, refine the scene shader.
4. Optional, offered but not requested: settings sliders for the film effects; investigating the "Unlimited" cap (NVIDIA control panel settings); lowering CPU use at 360 FPS (about 41% of one core, the driver waits actively on swap).

## Pitfalls met in this session

See "Pitfalls learned" in `CLAUDE.md`. The most costly ones: `.scr` launched through the shell always runs `/S`; stale captures in full screen (measure through the log); a real mouse move by the user during `/s` tests looks like a failure; long tests must run detached because tool calls time out after 10 minutes.

## Open questions

- Should the release workflow be added, and should it replace the v1.0.0 asset? (Unanswered.)
- Should the film effects be adjustable in the settings? (Offered, no answer.)
- The default bloom is 35 out of 100, chosen for the film look, while the specification asked for a discreet default. The user saw the comparison at 75 and did not ask for a change. Treated as accepted, not explicitly confirmed.
- Uncertain: whether the release notes on GitHub were edited after publication; only their beginning was re-read and it matches the draft from the session.
- The 30 minute memory run used the build just before the Orientation setting; the final code had a 15 minute run with the same pattern. A full 30 minute run on the exact v1.0.0 binary was not done.

## Notes for the next session

- Claude's memory files for this project (in the user's `.claude` folder) are empty: everything worth keeping is in `CLAUDE.md`, `HANDOFF.md` and the README.
- Local artifacts not in Git: `dist\`, `publish\`, `bin\`, `obj\`, `screenshots\raw\` (captures, memory CSV files). They can be regenerated.
