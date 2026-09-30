namespace ScreensaverStargate;

/// <summary>
/// The /p preview: a child window inside the small monitor of the Windows
/// screen saver dialog. It ignores input and exits when the parent goes away.
/// </summary>
internal sealed class PreviewWindow : NativeWindow
{
    readonly RenderLoop? _loop;

    public PreviewWindow(IntPtr parent, Settings settings, uint seed)
    {
        Native.GetClientRect(parent, out var rc);
        var cp = new CreateParams
        {
            Caption = "Screensaver Stargate preview",
            Parent = parent,
            Style = Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS | Native.WS_CLIPCHILDREN,
            ClassStyle = Native.CS_OWNDC,
            X = 0,
            Y = 0,
            Width = Math.Max(1, rc.Width),
            Height = Math.Max(1, rc.Height),
        };
        CreateHandle(cp);

        // The preview is tiny: no diagnostics, and the frame rate follows vsync.
        var s = settings.Clone();
        s.ShowDiagnostics = false;
        s.VerticalSync = true;
        s.FrameRateLimit = FrameRateLimit.Automatic;
        _loop = new RenderLoop(Handle, s, new RenderLoopOptions
        {
            Seed = seed,
            DeviceName = Screen.FromHandle(parent).DeviceName,
            Failed = _ => { },
        });
        _loop.Start();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_ERASEBKGND:
                m.Result = (IntPtr)1;
                return;
            case Native.WM_DESTROY:
                _loop?.Dispose();
                Application.ExitThread();
                break;
        }
        base.WndProc(ref m);
    }
}
