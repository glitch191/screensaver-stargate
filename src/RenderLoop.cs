using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreensaverStargate;

internal sealed class RenderLoopOptions
{
    public uint Seed;
    /// <summary>Display device name used to detect the refresh rate.</summary>
    public string? DeviceName;
    public bool ForceDiagnostics;
    /// <summary>When set, render one frame at <see cref="ScreenshotSize"/> and save it as PNG.</summary>
    public string? ScreenshotPath;
    public Size ScreenshotSize;
    /// <summary>Seconds of simulated time before the screenshot.</summary>
    public double ScreenshotTime = 3.0;
    /// <summary>Write memory statistics to the log every minute (diagnostics mode).</summary>
    public bool LogMemory;
    public Action<Exception>? Failed;
    public Action<string?>? ScreenshotDone;
}

/// <summary>
/// Renders the scene into one window on a dedicated thread with its own GL context.
/// Motion comes from measured elapsed time; the display is paced by vertical sync
/// and, when configured, by a frame rate cap.
/// </summary>
internal sealed class RenderLoop : IDisposable
{
    /// <summary>Wrap for the film effect time, short enough to keep float precision.</summary>
    const double FilmTimePeriod = 4096.0;

    readonly IntPtr _hwnd;
    readonly RenderLoopOptions _options;
    readonly Thread _thread;
    readonly ManualResetEventSlim _resume = new(true);
    volatile bool _stop;
    volatile Settings _settings;

    public RenderLoop(IntPtr hwnd, Settings settings, RenderLoopOptions options)
    {
        _hwnd = hwnd;
        _settings = settings.Clone();
        _options = options;
        _thread = new Thread(Run) { IsBackground = true, Name = "Render" };
    }

    /// <summary>Replaces the settings used from the next frame on.</summary>
    public Settings Settings
    {
        set => _settings = value.Clone();
    }

    public bool Paused
    {
        set { if (value) _resume.Reset(); else _resume.Set(); }
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stop = true;
        _resume.Set();
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
            _thread.Join(3000);
        _resume.Dispose();
    }

    void Run()
    {
        GlContext? ctx = null;
        SceneRenderer? renderer = null;
        IntPtr timer = IntPtr.Zero;
        try
        {
            ctx = new GlContext(_hwnd);
            renderer = new SceneRenderer();
            Log.Write($"Render context ready: {ctx.Renderer}");
        }
        catch (Exception ex)
        {
            Log.Write($"OpenGL 3.3 initialization failed: {ex.Message}");
            renderer?.Dispose();
            ctx?.Dispose();
            _options.Failed?.Invoke(ex);
            return;
        }

        try
        {
            if (_options.ScreenshotPath != null)
            {
                RunScreenshot(ctx, renderer);
                return;
            }

            timer = Native.CreateWaitableTimerExW(IntPtr.Zero, null, Native.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Native.TIMER_ALL_ACCESS);
            int refresh = _options.DeviceName != null ? Native.GetRefreshRate(_options.DeviceName) : 0;
            var sim = new Simulation();
            var diag = new Diagnostics();
            int swapInterval = -1;
            long freq = Stopwatch.Frequency;
            long last = Stopwatch.GetTimestamp();
            long deadline = last;
            long started = last;
            long frames = 0;

            while (!_stop)
            {
                if (!_resume.IsSet)
                {
                    _resume.Wait();
                    last = Stopwatch.GetTimestamp();
                    deadline = last;
                    continue;
                }

                Settings s = _settings;
                int interval = s.VerticalSync ? 1 : 0;
                if (interval != swapInterval)
                {
                    int applied = ctx.SetSwapInterval(interval);
                    swapInterval = interval;
                    if (applied != interval)
                        Log.Write($"Swap interval {interval} requested, driver reports {applied}.");
                }

                long now = Stopwatch.GetTimestamp();
                double elapsed = (double)(now - last) / freq;
                last = now;

                sim.ApplySettings(s);
                double alpha = sim.Advance(elapsed);

                Native.GetClientRect(_hwnd, out var rc);
                var p = FrameParams.From(s, rc.Width, rc.Height, _options.Seed);
                p.Scroll = (float)sim.InterpolatedScroll(alpha);
                p.ColorPhase = (float)sim.InterpolatedColor(alpha);
                p.Time = (float)(sim.InterpolatedTime(alpha) % FilmTimePeriod);
                p.Diagnostics |= _options.ForceDiagnostics;

                if (p.Diagnostics && elapsed > 0 &&
                    diag.AddFrame(elapsed, refresh, s.VerticalSync, s.FrameRateLimit, s.CustomFrameRate, rc.Width, rc.Height, s.WallMode, _options.LogMemory))
                    renderer.SetText(diag.Text);

                renderer.Render(p, offscreen: false);
                ctx.SwapBuffers();
                frames++;

                double cap = s.FrameRateLimit switch
                {
                    FrameRateLimit.Custom => s.CustomFrameRate,
                    FrameRateLimit.Automatic when !s.VerticalSync => refresh,
                    _ => 0,
                };
                if (cap > 0)
                    WaitForDeadline(ref deadline, (long)(freq / cap), timer);
                else
                    deadline = Stopwatch.GetTimestamp();
            }
            double seconds = (double)(Stopwatch.GetTimestamp() - started) / freq;
            Log.Write($"Render loop ended: {frames} frames in {seconds:F1} s ({frames / Math.Max(seconds, 1e-6):F1} FPS average).");
            if (diag.AverageMs > 0)
                Log.Write($"Diagnostics, last 2 s: refresh {refresh} Hz, frame average {diag.AverageMs:F2} ms, worst 1% {diag.Worst1PercentMs:F2} ms.");
        }
        catch (Exception ex)
        {
            Log.Write($"Render loop stopped: {ex}");
            _options.Failed?.Invoke(ex);
        }
        finally
        {
            if (timer != IntPtr.Zero) Native.CloseHandle(timer);
            renderer!.Dispose();
            ctx!.Dispose();
        }
    }

    /// <summary>Waits until the next frame deadline: coarse wait on a high resolution timer, then a short spin.</summary>
    static void WaitForDeadline(ref long deadline, long period, IntPtr timer)
    {
        long now = Stopwatch.GetTimestamp();
        deadline += period;
        if (deadline < now - period)
            deadline = now; // fell behind: do not try to catch up with a burst of frames
        long spinTicks = Stopwatch.Frequency / 2000; // last 0.5 ms
        long remaining = deadline - now;
        if (remaining > spinTicks && timer != IntPtr.Zero)
        {
            // Relative due time in 100 ns units, negative.
            long due = -(remaining - spinTicks) * 10_000_000 / Stopwatch.Frequency;
            if (Native.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                Native.WaitForSingleObject(timer, 1000);
        }
        while (Stopwatch.GetTimestamp() < deadline)
            Thread.SpinWait(20);
    }

    void RunScreenshot(GlContext ctx, SceneRenderer renderer)
    {
        Settings s = _settings;
        var sim = new Simulation();
        sim.ApplySettings(s);
        sim.AdvanceExact(_options.ScreenshotTime);
        var size = _options.ScreenshotSize;
        var p = FrameParams.From(s, size.Width, size.Height, _options.Seed);
        p.Scroll = (float)sim.InterpolatedScroll(1.0);
        p.ColorPhase = (float)sim.InterpolatedColor(1.0);
        p.Time = (float)(sim.InterpolatedTime(1.0) % FilmTimePeriod);
        // A single offscreen frame has no meaningful frame timing, so no overlay.
        p.Diagnostics = false;

        for (int i = 0; i < 3; i++)
        {
            renderer.Render(p, offscreen: true);
            Native.GetClientRect(_hwnd, out var rc);
            renderer.BlitOffscreenToWindow(rc.Width, rc.Height);
            ctx.SwapBuffers();
        }

        string? error = null;
        try
        {
            byte[] pixels = renderer.ReadOffscreen(out int w, out int h);
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++)
                Marshal.Copy(pixels, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            bmp.UnlockBits(data);
            string path = Path.GetFullPath(_options.ScreenshotPath!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bmp.Save(path, ImageFormat.Png);
            Log.Write($"Screenshot saved: {path} ({w}x{h})");
        }
        catch (Exception ex)
        {
            error = $"Screenshot could not be saved: {ex.Message}";
            Log.Write(error);
        }
        _options.ScreenshotDone?.Invoke(error);
    }
}
