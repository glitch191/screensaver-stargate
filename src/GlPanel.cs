using System.ComponentModel;

namespace ScreensaverStargate;

/// <summary>Live scene preview control for the settings window.</summary>
internal sealed class GlPanel : Control
{
    RenderLoop? _loop;
    Settings _settings = new();
    bool _paused;

    public event Action<Exception>? RenderFailed;

    public GlPanel()
    {
        SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Color.Black;
    }

    public string? FailureMessage { get; private set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Settings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            if (_loop != null) _loop.Settings = value;
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (_loop != null) _loop.Paused = value;
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
        if (DesignMode) return;
        _loop = new RenderLoop(Handle, _settings, new RenderLoopOptions
        {
            Seed = (uint)Random.Shared.Next(),
            DeviceName = Screen.FromHandle(Handle).DeviceName,
            Failed = ex => BeginInvoke(() => OnFailed(ex)),
        });
        _loop.Paused = _paused;
        _loop.Start();
    }

    void OnFailed(Exception ex)
    {
        StopLoop();
        FailureMessage = "Preview unavailable: OpenGL 3.3 could not be initialized.";
        Invalidate();
        RenderFailed?.Invoke(ex);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        StopLoop();
        base.OnHandleDestroyed(e);
    }

    void StopLoop()
    {
        _loop?.Dispose();
        _loop = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_loop != null) return;
        e.Graphics.Clear(Color.Black);
        if (FailureMessage != null)
            TextRenderer.DrawText(e.Graphics, FailureMessage, Font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopLoop();
        base.Dispose(disposing);
    }
}
