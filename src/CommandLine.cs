namespace ScreensaverStargate;

internal enum LaunchAction { Configure, Screensaver, Preview, Windowed }

/// <summary>
/// Parses the Windows screensaver protocol (/s, /c[:hwnd], /p hwnd) and the
/// development options (--windowed, --size, --screenshot, --seed, --diag, --mode, --orientation, --ui-scale).
/// </summary>
internal sealed class CommandLine
{
    public LaunchAction Action = LaunchAction.Configure;
    public IntPtr Handle;
    public Size? Size;
    public string? Screenshot;
    public uint? Seed;
    public bool Diagnostics;
    public WallMode? Mode;
    public ScrollOrientation? Orientation;
    public int UiScalePercent = 100;

    public const string Usage =
        "Usage:\n" +
        "  /s                 Start the screensaver on the configured screens.\n" +
        "  /c[:hwnd]          Open the settings window.\n" +
        "  /p <hwnd>          Draw the preview inside the given parent window.\n" +
        "Development options:\n" +
        "  --windowed         Show the scene in a normal window.\n" +
        "  --size WxH         Window or screenshot size in pixels, for example 1920x1080.\n" +
        "  --screenshot file  Save a PNG after 3 seconds of simulated time, then exit.\n" +
        "  --seed n           Fixed random seed for a reproducible scene.\n" +
        "  --diag             Show the diagnostics overlay.\n" +
        "  --mode perspective|flat  Override the wall mode for this run.\n" +
        "  --orientation horizontal|vertical  Override the scroll orientation for this run.\n" +
        "  --ui-scale percent Emulate a display scale for the settings window (100, 125, 150).";

    public static CommandLine Parse(string[] args)
    {
        var cl = new CommandLine();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string lower = arg.ToLowerInvariant();

            if (lower.StartsWith("--"))
            {
                switch (lower)
                {
                    case "--windowed":
                        cl.Action = LaunchAction.Windowed;
                        break;
                    case "--size":
                        cl.Size = ParseSize(Next(args, ref i, arg));
                        break;
                    case "--screenshot":
                        cl.Screenshot = Next(args, ref i, arg);
                        break;
                    case "--seed":
                        string seedText = Next(args, ref i, arg);
                        if (!uint.TryParse(seedText, out uint seed))
                            throw new ArgumentException($"--seed expects a whole number from 0 to {uint.MaxValue}, got \"{seedText}\".");
                        cl.Seed = seed;
                        break;
                    case "--diag":
                        cl.Diagnostics = true;
                        break;
                    case "--mode":
                        string mode = Next(args, ref i, arg).ToLowerInvariant();
                        cl.Mode = mode switch
                        {
                            "perspective" => WallMode.Perspective,
                            "flat" => WallMode.Flat,
                            _ => throw new ArgumentException($"--mode expects perspective or flat, got \"{mode}\"."),
                        };
                        break;
                    case "--orientation":
                        string orientation = Next(args, ref i, arg).ToLowerInvariant();
                        cl.Orientation = orientation switch
                        {
                            "horizontal" => ScrollOrientation.Horizontal,
                            "vertical" => ScrollOrientation.Vertical,
                            _ => throw new ArgumentException($"--orientation expects horizontal or vertical, got \"{orientation}\"."),
                        };
                        break;
                    case "--ui-scale":
                        string scaleText = Next(args, ref i, arg);
                        if (!int.TryParse(scaleText, out int pct) || pct < 100 || pct > 300)
                            throw new ArgumentException($"--ui-scale expects a percentage from 100 to 300, got \"{scaleText}\".");
                        cl.UiScalePercent = pct;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option \"{arg}\".");
                }
                continue;
            }

            if ((lower.StartsWith('/') || lower.StartsWith('-')) && lower.Length >= 2)
            {
                char flag = lower[1];
                string? inline = lower.Length > 3 && lower[2] == ':' ? arg[3..] : null;
                switch (flag)
                {
                    case 's':
                        cl.Action = LaunchAction.Screensaver;
                        break;
                    case 'c':
                        cl.Action = LaunchAction.Configure;
                        if (inline != null) cl.Handle = ParseHandle(inline);
                        else if (i + 1 < args.Length && long.TryParse(args[i + 1], out _)) cl.Handle = ParseHandle(args[++i]);
                        break;
                    case 'p':
                        cl.Action = LaunchAction.Preview;
                        cl.Handle = ParseHandle(inline ?? Next(args, ref i, arg));
                        break;
                    default:
                        throw new ArgumentException($"Unknown option \"{arg}\".");
                }
                continue;
            }

            throw new ArgumentException($"Unexpected argument \"{arg}\".");
        }

        if (cl.Screenshot != null && cl.Action == LaunchAction.Configure && !HasConfigureFlag(args))
            cl.Action = LaunchAction.Windowed;
        return cl;
    }

    static bool HasConfigureFlag(string[] args)
        => args.Any(a => a.Length >= 2 && (a[0] == '/' || (a[0] == '-' && a[1] != '-')) && char.ToLowerInvariant(a[1]) == 'c');

    static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Option \"{option}\" needs a value.");
        return args[++i];
    }

    static IntPtr ParseHandle(string text)
    {
        if (!long.TryParse(text, out long value) || value == 0)
            throw new ArgumentException($"Expected a window handle number, got \"{text}\".");
        return new IntPtr(value);
    }

    static Size ParseSize(string text)
    {
        var parts = text.ToLowerInvariant().Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) &&
            w >= 16 && h >= 16 && w <= 16384 && h <= 16384)
            return new Size(w, h);
        throw new ArgumentException($"--size expects WIDTHxHEIGHT between 16 and 16384, for example 1920x1080, got \"{text}\".");
    }
}
