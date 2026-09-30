namespace ScreensaverStargate;

/// <summary>
/// Scene logic advanced with a fixed time step, independent of the display rate.
/// The renderer reads an interpolated state between the last two steps.
/// Phases are kept in "key units" (one key = one color key along a line) and
/// wrapped by <see cref="Period"/>, which the shaders treat as the pattern period.
/// </summary>
public sealed class Simulation
{
    public const double Step = 1.0 / 240.0;
    public const double MaxFrameDelta = 0.25;
    public const double Period = 256.0;

    double _accumulator;
    double _scroll, _prevScroll;
    double _color, _prevColor;

    /// <summary>Scroll speed in key units per second.</summary>
    public double ScrollRate { get; set; }

    /// <summary>Color evolution speed in key units per second.</summary>
    public double ColorRate { get; set; }

    /// <summary>Total simulated time in seconds (for tests and screenshots).</summary>
    public double Time { get; private set; }

    /// <summary>Advances by a real elapsed time. Returns the interpolation factor for rendering.</summary>
    public double Advance(double elapsed)
    {
        if (elapsed < 0) elapsed = 0;
        if (elapsed > MaxFrameDelta) elapsed = MaxFrameDelta;
        _accumulator += elapsed;
        while (_accumulator >= Step)
        {
            StepOnce();
            _accumulator -= Step;
        }
        return _accumulator / Step;
    }

    /// <summary>Advances by an exact number of seconds of simulated time, without the frame cap.</summary>
    public void AdvanceExact(double seconds)
    {
        long steps = (long)Math.Round(seconds / Step);
        for (long i = 0; i < steps; i++)
            StepOnce();
        _accumulator = 0;
    }

    void StepOnce()
    {
        _prevScroll = _scroll;
        _prevColor = _color;
        _scroll += ScrollRate * Step;
        _color += ColorRate * Step;
        Time += Step;

        // Wrap both the previous and current values together so interpolation stays valid.
        if (_scroll >= Period || _scroll < 0)
        {
            double wraps = Math.Floor(_scroll / Period);
            _scroll -= wraps * Period;
            _prevScroll -= wraps * Period;
            _scrollWraps += (long)wraps;
        }
        if (_color >= Period)
        {
            _color -= Period;
            _prevColor -= Period;
            _colorWraps++;
        }
    }

    long _scrollWraps, _colorWraps;

    /// <summary>Simulated time matching the interpolated state, used by the film effects.</summary>
    public double InterpolatedTime(double alpha) => Math.Max(0.0, Time - Step * (1.0 - alpha));

    public double InterpolatedScroll(double alpha) => _prevScroll + (_scroll - _prevScroll) * alpha;

    public double InterpolatedColor(double alpha) => _prevColor + (_color - _prevColor) * alpha;

    /// <summary>Unwrapped scroll distance, used by tests to compare runs.</summary>
    public double TotalScroll(double alpha) => InterpolatedScroll(alpha) + _scrollWraps * Period;

    /// <summary>Unwrapped color phase, used by tests to compare runs.</summary>
    public double TotalColor(double alpha) => InterpolatedColor(alpha) + _colorWraps * Period;

    /// <summary>Converts user settings into rates.</summary>
    public void ApplySettings(Settings s)
    {
        double speed = s.Speed / 50.0; // 1.0 at the default
        double keysPerSecond = s.WallMode == WallMode.Perspective ? 1.4 : 1.0;
        double dir = s.Direction == ScrollDirection.TowardViewer ? 1.0 : -1.0;
        ScrollRate = speed * keysPerSecond * dir;
        ColorRate = s.ColorChangeSpeed / 40.0 * 0.25;
    }
}
