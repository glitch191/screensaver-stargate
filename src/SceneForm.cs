namespace ScreensaverStargate;

/// <summary>
/// A window showing the scene: borderless full screen on one monitor for /s,
/// or a normal window for the --windowed development option.
/// </summary>
internal sealed class SceneForm : Form
{
    const int MouseThreshold = 10; // pixels at 96 DPI

    static Point? _mouseOrigin;

    readonly Screen? _screen;
    readonly bool _screensaver;
    readonly bool _render;
    readonly Settings _settings;
    readonly RenderLoopOptions _options;
    RenderLoop? _loop;

    /// <summary>Creates a full screen window. When <paramref name="render"/> is false the screen stays black.</summary>
    public static SceneForm FullScreen(Screen screen, Settings settings, RenderLoopOptions options, bool render)
        => new(screen, null, settings, options, screensaver: true, render);

    public static SceneForm Windowed(Size clientSize, Settings settings, RenderLoopOptions options)
        => new(null, clientSize, settings, options, screensaver: false, render: true);

    SceneForm(Screen? screen, Size? clientSize, Settings settings, RenderLoopOptions options, bool screensaver, bool render)
    {
        _screen = screen;
        _settings = settings;
        _options = options;
        _screensaver = screensaver;
        _render = render;

        SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.Black;
        Text = "Screensaver Stargate";
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;

        if (screen != null)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds;
            ShowInTaskbar = false;
            TopMost = true;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = clientSize ?? new Size(1280, 720);
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= Native.CS_OWNDC;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_screen != null)
            ApplyScreenBounds();
        if (!_render)
            return;

        _options.DeviceName ??= (_screen ?? Screen.FromHandle(Handle)).DeviceName;
        _options.Failed = ex => BeginInvokeSafe(() => OnRenderFailed(ex));
        _loop = new RenderLoop(Handle, _settings, _options);
        _loop.Start();
    }

    void ApplyScreenBounds()
    {
        var b = _screen!.Bounds;
        Native.SetWindowPos(Handle, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    void OnRenderFailed(Exception ex)
    {
        if (_screensaver)
        {
            // Nothing can be shown; the cause is in the log.
            Application.Exit();
            return;
        }
        MessageBox.Show(this,
            "The scene could not be rendered because OpenGL 3.3 is not available.\n\n" +
            $"Details: {ex.Message}\n\nUpdate the graphics driver and try again. Log file: {Log.FilePath}",
            "Screensaver Stargate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Close();
    }

    void BeginInvokeSafe(Action action)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _loop?.Dispose();
        _loop = null;
        base.OnFormClosing(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (!_render)
            e.Graphics.Clear(Color.Black);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_screensaver) Application.Exit();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_screensaver) Application.Exit();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_screensaver) return;
        Point pos = Cursor.Position;
        // The first position seen is the origin, which absorbs the initial move event.
        _mouseOrigin ??= pos;
        int threshold = MouseThreshold * DeviceDpi / 96;
        if (Math.Abs(pos.X - _mouseOrigin.Value.X) > threshold || Math.Abs(pos.Y - _mouseOrigin.Value.Y) > threshold)
            Application.Exit();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_SYSCOMMAND when _screensaver:
                int cmd = (int)m.WParam & 0xFFF0;
                if (cmd == Native.SC_SCREENSAVE) { m.Result = IntPtr.Zero; return; }
                break;
            case Native.WM_DPICHANGED when _screen != null:
                // Keep exact monitor bounds instead of the size suggested for the new DPI.
                ApplyScreenBounds();
                m.Result = IntPtr.Zero;
                return;
            case Native.WM_SETCURSOR when _screensaver:
                Cursor.Current = null;
                m.Result = (IntPtr)1;
                return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Sets the mouse origin used by the movement threshold.</summary>
    public static void SetMouseOrigin(Point p) => _mouseOrigin = p;
}
