namespace ScreensaverStargate;

/// <summary>Minimal append-only log in %APPDATA%\ScreensaverStargate\log.txt.</summary>
internal static class Log
{
    const long MaxBytes = 1024 * 1024;
    static readonly object Sync = new();

    public static string FilePath => Path.Combine(Settings.Folder, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Settings.Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    info.Delete();
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the screensaver down.
        }
    }
}
