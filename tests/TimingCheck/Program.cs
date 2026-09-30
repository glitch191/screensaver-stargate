using ScreensaverStargate;

// Checks that motion depends only on elapsed time, never on the frame rate:
// the same wall-clock duration gives the same scroll and color phase at every
// refresh rate, with jitter, and the per-frame change stays proportional to dt.

const double Duration = 30.0;
var settings = new Settings();
double expectedScroll, expectedColor;
{
    var reference = new Simulation();
    reference.ApplySettings(settings);
    expectedScroll = reference.ScrollRate * Duration;
    expectedColor = reference.ColorRate * Duration;
}

int failures = 0;
var rng = new Random(1234);

void Check(string name, Func<int, double> frameTime)
{
    var sim = new Simulation();
    sim.ApplySettings(settings);
    double t = 0, alpha = 0, prev = 0, maxJump = 0, maxJumpDt = 0;
    int frame = 0;
    while (t < Duration - 1e-9)
    {
        double dt = Math.Min(frameTime(frame++), Duration - t);
        t += dt;
        alpha = sim.Advance(dt);
        double pos = sim.TotalScroll(alpha);
        double jump = Math.Abs(pos - prev);
        if (jump > maxJump) { maxJump = jump; maxJumpDt = dt; }
        prev = pos;
    }
    double scroll = sim.TotalScroll(alpha);
    double color = sim.TotalColor(alpha);
    double scrollErr = Math.Abs(scroll - expectedScroll);
    double colorErr = Math.Abs(color - expectedColor);
    // Interpolation lags by at most one fixed step.
    double tol = Math.Abs(settings.Speed / 50.0 * 1.4) * Simulation.Step * 1.01 + 1e-9;
    // The largest per-frame move must match the largest frame time (capped at MaxFrameDelta).
    double maxAllowedJump = Math.Abs(sim.ScrollRate) * (Math.Min(maxJumpDt, Simulation.MaxFrameDelta) + Simulation.Step) + 1e-9;
    bool ok = scrollErr <= tol && colorErr <= tol && maxJump <= maxAllowedJump;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-34} frames {frame,6}  scroll {scroll,9:F5} (expected {expectedScroll:F5}, error {scrollErr:E1})  color error {colorErr:E1}  max step {maxJump:F5}");
}

Check("60 Hz", _ => 1.0 / 60.0);
Check("144 Hz", _ => 1.0 / 144.0);
Check("240 Hz", _ => 1.0 / 240.0);
Check("360 Hz", _ => 1.0 / 360.0);
Check("59.94 Hz", _ => 1.0 / 59.94);
Check("165 Hz with +/-30% jitter", _ => 1.0 / 165.0 * (0.7 + 0.6 * rng.NextDouble()));
Check("360 Hz with a 100 ms hitch every 5 s", f => f % 1800 == 900 ? 0.1 : 1.0 / 360.0);

// A long stall (sleep, debugger) must not produce a jump: the frame delta is capped.
{
    var sim = new Simulation();
    sim.ApplySettings(settings);
    sim.Advance(1.0 / 60.0);
    double before = sim.TotalScroll(0);
    double a = sim.Advance(10.0);
    double moved = sim.TotalScroll(a) - before;
    double cap = sim.ScrollRate * (Simulation.MaxFrameDelta + Simulation.Step);
    bool ok = moved <= cap + 1e-9;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  10 s stall is capped                 moved {moved:F5} (cap {cap:F5})");
}

// Wrapping by the pattern period keeps the interpolated values continuous.
{
    var sim = new Simulation();
    settings.Speed = 100;
    sim.ApplySettings(settings);
    double last = sim.InterpolatedScroll(0);
    double worst = 0;
    for (int i = 0; i < 360 * 200; i++)
    {
        double a = sim.Advance(1.0 / 360.0);
        double cur = sim.InterpolatedScroll(a);
        double d = cur - last;
        d -= Math.Round(d / Simulation.Period) * Simulation.Period; // a wrap is an exact multiple of the period
        worst = Math.Max(worst, Math.Abs(d));
        last = cur;
    }
    bool ok = worst < sim.ScrollRate * (1.0 / 360.0 + Simulation.Step) + 1e-9;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  wrap continuity over 200 s           largest step {worst:F5} key units");
}

Console.WriteLine(failures == 0 ? "All timing checks passed." : $"{failures} timing check(s) failed.");
return failures == 0 ? 0 : 1;
