namespace ScreensaverStargate;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write($"Unhandled UI exception: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Unhandled exception: {e.ExceptionObject}");

        CommandLine cl;
        try
        {
            cl = CommandLine.Parse(args);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show($"{ex.Message}\n\n{CommandLine.Usage}", "Screensaver Stargate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        var settings = Settings.Load();
        if (cl.Mode.HasValue)
            settings.WallMode = cl.Mode.Value;
        if (cl.Orientation.HasValue)
            settings.Orientation = cl.Orientation.Value;
        uint seed = cl.Seed ?? (uint)(Environment.TickCount64 ^ ((long)Environment.ProcessId << 16));

        return cl.Action switch
        {
            LaunchAction.Screensaver => RunScreensaver(settings, seed, cl),
            LaunchAction.Preview => RunPreview(settings, seed, cl.Handle),
            LaunchAction.Windowed => RunWindowed(settings, seed, cl),
            _ => RunConfigure(settings, cl),
        };
    }

    /// <summary>Mixes the base seed with the screen index so every screen gets its own pattern.</summary>
    static uint SeedFor(uint seed, int index) => seed ^ (uint)(index * unchecked((int)0x9E3779B9));

    static int RunScreensaver(Settings settings, uint seed, CommandLine cl)
    {
        using var mutex = new Mutex(true, @"Local\ScreensaverStargate.Scene", out bool created);
        if (!created)
            return 0; // already running

        var screens = Screen.AllScreens;
        var forms = new List<SceneForm>();
        for (int i = 0; i < screens.Length; i++)
        {
            bool render = settings.Screens == ScreenSelection.AllScreens || screens[i].Primary;
            forms.Add(SceneForm.FullScreen(screens[i], settings, new RenderLoopOptions
            {
                Seed = SeedFor(seed, i),
                ForceDiagnostics = cl.Diagnostics,
                LogMemory = cl.Diagnostics || settings.ShowDiagnostics,
            }, render));
        }
        Log.Write($"Screensaver started on {forms.Count} screen(s), mode {settings.WallMode}, screens {settings.Screens}.");

        SceneForm.SetMouseOrigin(Cursor.Position);
        var context = new ApplicationContext();
        foreach (var f in forms)
        {
            f.FormClosed += (_, _) => context.ExitThread();
            f.Show();
        }
        forms.Find(f => Screen.FromControl(f).Primary)?.Activate();
        Cursor.Hide();
        Application.Run(context);
        foreach (var f in forms)
            f.Dispose();
        return 0;
    }

    static int RunPreview(Settings settings, uint seed, IntPtr parent)
    {
        if (!Native.IsWindow(parent))
            return 1;
        var preview = new PreviewWindow(parent, settings, seed);
        Application.Run();
        GC.KeepAlive(preview);
        return 0;
    }

    static int RunWindowed(Settings settings, uint seed, CommandLine cl)
    {
        int exitCode = 0;
        var options = new RenderLoopOptions
        {
            Seed = seed,
            ForceDiagnostics = cl.Diagnostics,
            LogMemory = cl.Diagnostics || settings.ShowDiagnostics,
        };

        Size windowSize = cl.Size ?? new Size(1280, 720);
        if (cl.Screenshot != null)
        {
            // Render offscreen at the exact size; the window only shows a scaled copy.
            Size target = cl.Size ?? new Size(1920, 1080);
            options.ScreenshotPath = cl.Screenshot;
            options.ScreenshotSize = target;
            var area = Screen.PrimaryScreen!.WorkingArea;
            double fit = Math.Min(1.0, Math.Min(area.Width * 0.8 / target.Width, area.Height * 0.8 / target.Height));
            windowSize = new Size((int)(target.Width * fit), (int)(target.Height * fit));
        }

        using var form = SceneForm.Windowed(windowSize, settings, options);
        options.ScreenshotDone = error => form.BeginInvoke(() =>
        {
            exitCode = error == null ? 0 : 1;
            form.Close();
        });
        Application.Run(form);
        return exitCode;
    }

    static int RunConfigure(Settings settings, CommandLine cl)
    {
        using var form = new SettingsForm(settings, cl.UiScalePercent, cl.Screenshot);
        if (cl.Handle != IntPtr.Zero && Native.IsWindow(cl.Handle))
            form.ShowDialog(new OwnerWindow(cl.Handle));
        else
            Application.Run(form);
        return 0;
    }

    sealed class OwnerWindow(IntPtr handle) : IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
