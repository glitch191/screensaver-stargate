using System.Diagnostics;

namespace ScreensaverStargate;

/// <summary>
/// Frame time statistics for the diagnostics overlay. All buffers are allocated
/// once; the text is rebuilt twice per second without allocating.
/// </summary>
internal sealed class Diagnostics
{
    const int Capacity = 4096;
    const double WindowSeconds = 2.0;
    const double UpdateInterval = 0.5;
    const double MemoryLogInterval = 60.0;

    readonly float[] _frames = new float[Capacity];
    readonly float[] _scratch = new float[Capacity];
    int _next, _count;
    double _sinceUpdate;
    double _sinceMemoryLog;
    double _uptime;
    int _cursor;

    public int[] Text { get; } = new int[SceneRenderer.TextCols * SceneRenderer.TextRows];

    public double AverageMs { get; private set; }
    public double Worst1PercentMs { get; private set; }

    /// <summary>Records one frame. Returns true when the text was rebuilt.</summary>
    public bool AddFrame(double seconds, int refreshHz, bool vsync, FrameRateLimit limit, int customFps, int width, int height, WallMode mode, bool logMemory)
    {
        _frames[_next] = (float)seconds;
        _next = (_next + 1) % Capacity;
        if (_count < Capacity) _count++;

        _uptime += seconds;
        if (logMemory)
        {
            _sinceMemoryLog += seconds;
            if (_sinceMemoryLog >= MemoryLogInterval)
            {
                _sinceMemoryLog = 0;
                LogMemory();
            }
        }

        _sinceUpdate += seconds;
        if (_sinceUpdate < UpdateInterval)
            return false;
        _sinceUpdate = 0;

        // Collect the frames of the last WindowSeconds, newest first.
        int n = 0;
        double total = 0;
        for (int i = 0; i < _count && total < WindowSeconds; i++)
        {
            float f = _frames[(_next - 1 - i + Capacity) % Capacity];
            _scratch[n++] = f;
            total += f;
        }
        if (n == 0) return false;
        AverageMs = total / n * 1000.0;
        Array.Sort(_scratch, 0, n);
        int worst = Math.Max(1, n / 100);
        double sumWorst = 0;
        for (int i = n - worst; i < n; i++) sumWorst += _scratch[i];
        Worst1PercentMs = sumWorst / worst * 1000.0;

        BuildText(refreshHz, vsync, limit, customFps, width, height, mode, n / total);
        return true;
    }

    void BuildText(int refreshHz, bool vsync, FrameRateLimit limit, int customFps, int width, int height, WallMode mode, double fps)
    {
        Array.Clear(Text);
        _cursor = 0;
        Put("REFRESH ");
        if (refreshHz > 0) { PutInt(refreshHz); Put(" HZ"); } else Put("UNKNOWN");
        Put("  VSYNC ");
        Put(vsync ? "ON" : "OFF");
        Put("  LIMIT ");
        switch (limit)
        {
            case FrameRateLimit.Automatic: Put("AUTO"); break;
            case FrameRateLimit.Unlimited: Put("NONE"); break;
            default: PutInt(customFps); break;
        }

        NewLine(1);
        Put("FRAME AVG ");
        PutFixed(AverageMs, 2);
        Put(" MS  (");
        PutFixed(fps, 1);
        Put(" FPS)");

        NewLine(2);
        Put("WORST 1% ");
        PutFixed(Worst1PercentMs, 2);
        Put(" MS");

        NewLine(3);
        PutInt(width);
        Put("X");
        PutInt(height);
        Put(mode == WallMode.Perspective ? "  PERSPECTIVE" : "  FLAT");
        Put("  UP ");
        PutInt((int)(_uptime / 60));
        Put(" MIN");
    }

    void NewLine(int row) => _cursor = row * SceneRenderer.TextCols;

    void Put(string s)
    {
        int rowEnd = (_cursor / SceneRenderer.TextCols + 1) * SceneRenderer.TextCols;
        foreach (char c in s)
        {
            if (_cursor >= rowEnd) return;
            Text[_cursor++] = GlyphFont.IndexOf(c);
        }
    }

    void PutChar(char c)
    {
        int rowEnd = (_cursor / SceneRenderer.TextCols + 1) * SceneRenderer.TextCols;
        if (_cursor < rowEnd) Text[_cursor++] = GlyphFont.IndexOf(c);
    }

    void PutInt(long v)
    {
        if (v < 0) { PutChar('-'); v = -v; }
        long div = 1;
        while (v / div >= 10) div *= 10;
        while (div > 0)
        {
            PutChar((char)('0' + v / div % 10));
            div /= 10;
        }
    }

    void PutFixed(double v, int decimals)
    {
        long scale = decimals == 1 ? 10 : 100;
        long scaled = (long)Math.Round(v * scale);
        PutInt(scaled / scale);
        PutChar('.');
        long frac = scaled % scale;
        if (decimals == 2 && frac < 10) PutChar('0');
        PutInt(frac);
    }

    static void LogMemory()
    {
        using var p = Process.GetCurrentProcess();
        Log.Write($"Memory: private {p.PrivateMemorySize64 / 1024 / 1024} MB, working set {p.WorkingSet64 / 1024 / 1024} MB, " +
                  $"managed heap {GC.GetTotalMemory(false) / 1024} KB, total allocated {GC.GetTotalAllocatedBytes() / 1024} KB, " +
                  $"gen0 collections {GC.CollectionCount(0)}");
    }
}
