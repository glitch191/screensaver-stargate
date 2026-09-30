using System.Drawing.Imaging;

namespace ScreensaverStargate;

/// <summary>The /c settings dialog with a live preview of the scene.</summary>
internal sealed class SettingsForm : Form
{
    const int MinTarget = 32; // minimum height of clickable controls, pixels at 96 DPI

    static readonly int[] FpsPresets = { 30, 60, 75, 90, 100, 120, 144, 165, 180, 200, 240, 280, 300, 360, 480, 500 };

    readonly float _uiScale;
    readonly string? _screenshotPath;
    readonly ToolTip _tips = new() { AutoPopDelay = 15000, InitialDelay = 400 };
    Settings _working;
    bool _loading;

    readonly RadioButton _perspective = Radio("Perspective"), _flat = Radio("Flat");
    readonly RadioButton _toward = Radio("Toward viewer"), _away = Radio("Away from viewer");
    readonly RadioButton _horizontal = Radio("Horizontal"), _vertical = Radio("Vertical");
    readonly RadioButton _soft = Radio("Soft"), _sharp = Radio("Sharp");
    readonly RadioButton _allScreens = Radio("All screens"), _primaryOnly = Radio("Primary screen only");
    readonly RadioButton _fpsAuto = Radio("Automatic"), _fpsCustom = Radio("Custom value"), _fpsUnlimited = Radio("Unlimited");
    readonly CheckBox _vsync = Check("Vertical sync"), _diag = Check("Show diagnostics");
    readonly ComboBox _customFps = new();
    readonly GlPanel _preview = new();
    readonly Button _reset = CreateButton("Reset to defaults"), _ok = CreateButton("OK"), _cancel = CreateButton("Cancel");

    TrackBar _speed = null!, _density = null!, _thickness = null!, _colorSpeed = null!, _bloom = null!, _lineWidth = null!;
    Label _speedValue = null!, _densityValue = null!, _thicknessValue = null!, _colorSpeedValue = null!, _bloomValue = null!, _lineWidthValue = null!;

    public SettingsForm(Settings settings, int uiScalePercent, string? screenshotPath)
    {
        _working = settings.Clone();
        _uiScale = uiScalePercent / 100f;
        _screenshotPath = screenshotPath;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f * _uiScale);
        Text = "Screensaver Stargate Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var root = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10),
            Location = Point.Empty,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(Column(BuildSceneGroup(), BuildCenterLineGroup()), 0, 0);
        root.Controls.Add(Column(BuildPreviewGroup(), BuildDisplayGroup()), 1, 0);
        var buttons = BuildButtonBar();
        root.Controls.Add(buttons, 0, 1);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);

        AcceptButton = _ok;
        CancelButton = _cancel;
        ResumeLayout(false);
        PerformLayout();

        LoadControls();
        _preview.Settings = _working;
        _preview.RenderFailed += ex => MessageBox.Show(this,
            "The live preview is unavailable because OpenGL 3.3 could not be initialized.\n\n" +
            $"Details: {ex.Message}\n\nThe settings can still be changed and saved. Updating the graphics driver usually fixes this. Log file: {Log.FilePath}",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ----- Layout -----

    GroupBox BuildSceneGroup()
    {
        var grid = Grid();
        AddChoice(grid, "Wall mode", _perspective, _flat);
        AddChoice(grid, "Orientation", _horizontal, _vertical);
        AddChoice(grid, "Direction", _toward, _away);
        (_speed, _speedValue) = AddSlider(grid, "Speed", 0, 100);
        (_density, _densityValue) = AddSlider(grid, "Line density", 0, 100);
        (_thickness, _thicknessValue) = AddSlider(grid, "Line thickness", 0, 100);
        (_colorSpeed, _colorSpeedValue) = AddSlider(grid, "Color change speed", 0, 100);
        (_bloom, _bloomValue) = AddSlider(grid, "Bloom intensity", 0, 100);
        _tips.SetToolTip(_perspective, "Two walls of lines converging to a vanishing point at the center.");
        _tips.SetToolTip(_flat, "Parallel horizontal lines scrolling sideways, without perspective.");
        _tips.SetToolTip(_horizontal, "Walls on the left and right, light streaming sideways, vertical center line.");
        _tips.SetToolTip(_vertical, "Walls above and below, light streaming up and down, horizontal center line.");
        _tips.SetToolTip(_toward, "Light streams from the center line toward the screen edges.");
        _tips.SetToolTip(_away, "Light streams from the screen edges toward the center line.");
        _tips.SetToolTip(_bloom, "Glow around the lines. 0 turns the glow off.");
        _tips.SetToolTip(_colorSpeed, "How fast the colors fade into new colors. 0 keeps the colors fixed along the lines.");
        return Group("Scene", grid);
    }

    GroupBox BuildCenterLineGroup()
    {
        var grid = Grid();
        (_lineWidth, _lineWidthValue) = AddSlider(grid, "Center line width", Settings.CenterLineMin, Settings.CenterLineMax);
        AddChoice(grid, "Center line edge", _soft, _sharp);
        _tips.SetToolTip(_lineWidth, "Width in pixels on a 1080-pixel-high screen. It scales with the screen height.");
        return Group("Center line", grid);
    }

    GroupBox BuildPreviewGroup()
    {
        _preview.Size = new Size(448, 252);
        _preview.MinimumSize = _preview.Size;
        _preview.Margin = new Padding(4);
        _preview.Anchor = AnchorStyles.None;
        var grid = Grid();
        grid.ColumnCount = 1;
        grid.Controls.Add(_preview, 0, 0);
        return Group("Preview", grid);
    }

    GroupBox BuildDisplayGroup()
    {
        var grid = Grid();
        AddChoice(grid, "Screens", _allScreens, _primaryOnly);
        AddChoice(grid, "Frame rate limit", _fpsAuto, _fpsCustom, _fpsUnlimited);

        _customFps.DropDownStyle = ComboBoxStyle.DropDown;
        _customFps.DrawMode = DrawMode.OwnerDrawFixed;
        _customFps.MinimumSize = new Size(96, 0);
        _customFps.Anchor = AnchorStyles.Left;
        _customFps.MaxLength = 4;
        foreach (int fps in FpsPresets)
            _customFps.Items.Add(fps.ToString());
        _customFps.DrawItem += DrawFpsItem;
        var fpsRow = Flow();
        fpsRow.Controls.Add(_customFps);
        fpsRow.Controls.Add(new Label { Text = "frames per second", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 0, 0, 0) });
        AddRow(grid, "Custom frame rate", fpsRow);

        AddWide(grid, _vsync);
        AddWide(grid, _diag);

        _tips.SetToolTip(_fpsAuto, "Uses the refresh rate of each screen. With vertical sync on, the screen sets the pace.");
        _tips.SetToolTip(_fpsCustom, $"Caps the frame rate at the value below ({Settings.CustomFpsMin} to {Settings.CustomFpsMax}).");
        _tips.SetToolTip(_fpsUnlimited, "No cap. With vertical sync on, the refresh rate still limits the frame rate.");
        _tips.SetToolTip(_customFps, $"A whole number from {Settings.CustomFpsMin} to {Settings.CustomFpsMax}.");
        _tips.SetToolTip(_vsync, "Synchronizes each frame with the refresh of its screen. Recommended.");
        _tips.SetToolTip(_diag, "Shows the detected refresh rate, the average frame time and the worst 1% of frame times.");
        _tips.SetToolTip(_primaryOnly, "Other screens stay black.");
        return Group("Display", grid);
    }

    Control BuildButtonBar()
    {
        var bar = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(_reset, 0, 0);
        bar.Controls.Add(_ok, 2, 0);
        bar.Controls.Add(_cancel, 3, 0);

        _reset.Click += (_, _) =>
        {
            _working = new Settings();
            LoadControls();
            _preview.Settings = _working;
        };
        _ok.Click += (_, _) => Accept();
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        return bar;
    }

    static Control Column(params Control[] children)
    {
        var col = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = children.Length,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        col.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var c in children)
        {
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.Controls.Add(c);
        }
        return col;
    }

    static GroupBox Group(string title, Control content)
    {
        var box = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(8, 6, 8, 6),
            Margin = new Padding(4),
        };
        box.Controls.Add(content);
        return box;
    }

    static TableLayoutPanel Grid()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    static FlowLayoutPanel Flow() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        Margin = new Padding(0),
        Anchor = AnchorStyles.Left,
    };

    static Label RowLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        MinimumSize = new Size(140, 0),
        Margin = new Padding(3, 3, 12, 3),
    };

    void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(RowLabel(label), 0, row);
        grid.Controls.Add(control, 1, row);
        grid.SetColumnSpan(control, 2);
    }

    void AddChoice(TableLayoutPanel grid, string label, params RadioButton[] options)
    {
        var flow = Flow();
        foreach (var r in options)
        {
            flow.Controls.Add(r);
            r.CheckedChanged += (_, _) => { if (r.Checked) OnChanged(); };
        }
        AddRow(grid, label, flow);
    }

    (TrackBar, Label) AddSlider(TableLayoutPanel grid, string label, int min, int max)
    {
        var bar = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = max - min >= 50 ? 10 : 5,
            SmallChange = 1,
            LargeChange = max - min >= 50 ? 10 : 5,
            AutoSize = true,
            MinimumSize = new Size(240, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        var value = new Label
        {
            AutoSize = true,
            MinimumSize = new Size(48, 0),
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(RowLabel(label), 0, row);
        grid.Controls.Add(bar, 1, row);
        grid.Controls.Add(value, 2, row);
        bar.ValueChanged += (_, _) => OnChanged();
        return (bar, value);
    }

    void AddWide(TableLayoutPanel grid, CheckBox check)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(check, 0, row);
        grid.SetColumnSpan(check, 3);
        check.CheckedChanged += (_, _) => OnChanged();
    }

    static RadioButton Radio(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(0, MinTarget),
        Margin = new Padding(0, 0, 12, 0),
    };

    static CheckBox Check(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(0, MinTarget),
        Anchor = AnchorStyles.Left,
    };

    static Button CreateButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowOnly,
        MinimumSize = new Size(96, MinTarget),
        Padding = new Padding(8, 0, 8, 0),
        Margin = new Padding(6, 3, 0, 3),
    };

    void DrawFpsItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index >= 0)
        {
            var color = (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : SystemColors.WindowText;
            TextRenderer.DrawText(e.Graphics, _customFps.Items[e.Index]!.ToString(), e.Font, e.Bounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        e.DrawFocusRectangle();
    }

    void UpdateComboHeight()
    {
        float scale = DeviceDpi / 96f * _uiScale;
        int target = (int)Math.Round(MinTarget * scale) - 6;
        _customFps.ItemHeight = Math.Max(target, Font.Height + 4);
    }

    // ----- Behavior -----

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (Math.Abs(_uiScale - 1f) > 0.001f)
            Scale(new SizeF(_uiScale, _uiScale)); // development option: emulate a display scale
        UpdateComboHeight();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateComboHeight();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _customFps.SelectionLength = 0;
        if (_screenshotPath != null)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (_, _) =>
            {
                timer.Dispose();
                SaveWindowScreenshot(_screenshotPath);
                Close();
            };
            timer.Start();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _preview.Paused = WindowState == FormWindowState.Minimized;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _preview.Paused = true;
        _tips.Dispose();
        base.OnFormClosed(e);
    }

    void LoadControls()
    {
        _loading = true;
        var s = _working;
        (s.WallMode == WallMode.Perspective ? _perspective : _flat).Checked = true;
        (s.Direction == ScrollDirection.TowardViewer ? _toward : _away).Checked = true;
        (s.Orientation == ScrollOrientation.Horizontal ? _horizontal : _vertical).Checked = true;
        (s.CenterLineEdge == CenterLineEdge.Soft ? _soft : _sharp).Checked = true;
        (s.Screens == ScreenSelection.AllScreens ? _allScreens : _primaryOnly).Checked = true;
        (s.FrameRateLimit switch
        {
            FrameRateLimit.Custom => _fpsCustom,
            FrameRateLimit.Unlimited => _fpsUnlimited,
            _ => _fpsAuto,
        }).Checked = true;
        _speed.Value = s.Speed;
        _density.Value = s.LineDensity;
        _thickness.Value = s.LineThickness;
        _colorSpeed.Value = s.ColorChangeSpeed;
        _bloom.Value = s.BloomIntensity;
        _lineWidth.Value = s.CenterLineWidth;
        _customFps.Text = s.CustomFrameRate.ToString();
        _vsync.Checked = s.VerticalSync;
        _diag.Checked = s.ShowDiagnostics;
        _loading = false;
        UpdateValueLabels();
    }

    void OnChanged()
    {
        if (_loading) return;
        ReadControls();
        UpdateValueLabels();
        _preview.Settings = _working;
    }

    void ReadControls()
    {
        var s = _working;
        s.WallMode = _perspective.Checked ? WallMode.Perspective : WallMode.Flat;
        s.Direction = _toward.Checked ? ScrollDirection.TowardViewer : ScrollDirection.AwayFromViewer;
        s.Orientation = _horizontal.Checked ? ScrollOrientation.Horizontal : ScrollOrientation.Vertical;
        s.CenterLineEdge = _soft.Checked ? CenterLineEdge.Soft : CenterLineEdge.Sharp;
        s.Screens = _allScreens.Checked ? ScreenSelection.AllScreens : ScreenSelection.PrimaryOnly;
        s.FrameRateLimit = _fpsCustom.Checked ? FrameRateLimit.Custom : _fpsUnlimited.Checked ? FrameRateLimit.Unlimited : FrameRateLimit.Automatic;
        s.Speed = _speed.Value;
        s.LineDensity = _density.Value;
        s.LineThickness = _thickness.Value;
        s.ColorChangeSpeed = _colorSpeed.Value;
        s.BloomIntensity = _bloom.Value;
        s.CenterLineWidth = _lineWidth.Value;
        if (TryParseFps(out int fps)) s.CustomFrameRate = fps;
        s.VerticalSync = _vsync.Checked;
        s.ShowDiagnostics = _diag.Checked;
    }

    void UpdateValueLabels()
    {
        _speedValue.Text = _speed.Value.ToString();
        _densityValue.Text = _density.Value.ToString();
        _thicknessValue.Text = _thickness.Value.ToString();
        _colorSpeedValue.Text = _colorSpeed.Value.ToString();
        _bloomValue.Text = _bloom.Value.ToString();
        _lineWidthValue.Text = $"{_lineWidth.Value} px";
        _customFps.Enabled = _fpsCustom.Checked;
    }

    bool TryParseFps(out int fps)
        => int.TryParse(_customFps.Text.Trim(), out fps) && fps >= Settings.CustomFpsMin && fps <= Settings.CustomFpsMax;

    void Accept()
    {
        if (_fpsCustom.Checked && !TryParseFps(out _))
        {
            MessageBox.Show(this,
                $"Custom frame rate must be a whole number from {Settings.CustomFpsMin} to {Settings.CustomFpsMax}, for example 144.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _customFps.Focus();
            _customFps.SelectAll();
            return;
        }
        ReadControls();
        _working.Validate();
        string? error = _working.Save();
        if (error != null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _customFps.TextChanged += (_, _) => { if (TryParseFps(out _)) OnChanged(); };
    }

    void SaveWindowScreenshot(string path)
    {
        try
        {
            // PrintWindow captures only this window, even when something covers it.
            var bounds = Bounds;
            using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                Native.PrintWindow(Handle, hdc, Native.PW_RENDERFULLCONTENT);
                g.ReleaseHdc(hdc);
            }
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            bmp.Save(full, ImageFormat.Png);
            Log.Write($"Settings screenshot saved: {full} ({bounds.Width}x{bounds.Height})");
        }
        catch (Exception ex)
        {
            Log.Write($"Settings screenshot failed: {ex.Message}");
        }
    }
}
