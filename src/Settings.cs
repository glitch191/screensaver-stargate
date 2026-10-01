using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreensaverStargate;

public enum WallMode { Perspective, Flat }
public enum CenterLineEdge { Soft, Sharp }
public enum ScrollDirection { TowardViewer, AwayFromViewer }
public enum ScrollOrientation { Horizontal, Vertical }
public enum ScreenSelection { AllScreens, PrimaryOnly }
public enum FrameRateLimit { Automatic, Custom, Unlimited }

/// <summary>User settings, stored in %APPDATA%\ScreensaverStargate\settings.json.</summary>
public sealed class Settings
{
    public const int SliderMin = 0;
    public const int SliderMax = 100;
    public const int CenterLineMin = 1;
    public const int CenterLineMax = 40;
    public const int CustomFpsMin = 20;
    public const int CustomFpsMax = 1000;

    public WallMode WallMode { get; set; } = WallMode.Perspective;
    public int Speed { get; set; } = 50;
    public int LineDensity { get; set; } = 50;
    public int LineThickness { get; set; } = 40;
    public int ColorChangeSpeed { get; set; } = 40;
    public int BloomIntensity { get; set; } = 35;
    // Film treatment strengths: 50 is the reference look, 0 turns the effect off, 100 doubles it.
    public int FilmGrain { get; set; } = 50;
    public int FilmGateWeave { get; set; } = 50;
    public int FilmLensSoftness { get; set; } = 50;
    public int FilmHalation { get; set; } = 50;
    public int FilmFlicker { get; set; } = 50;
    /// <summary>Center line width in pixels at 1080 pixels of screen height.</summary>
    public int CenterLineWidth { get; set; } = 6;
    public CenterLineEdge CenterLineEdge { get; set; } = CenterLineEdge.Soft;
    public ScrollDirection Direction { get; set; } = ScrollDirection.TowardViewer;
    /// <summary>Horizontal: walls left and right, vertical center line. Vertical: walls above and below.</summary>
    public ScrollOrientation Orientation { get; set; } = ScrollOrientation.Horizontal;
    public ScreenSelection Screens { get; set; } = ScreenSelection.AllScreens;
    public FrameRateLimit FrameRateLimit { get; set; } = FrameRateLimit.Automatic;
    public int CustomFrameRate { get; set; } = 144;
    public bool VerticalSync { get; set; } = true;
    public bool ShowDiagnostics { get; set; }

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreensaverStargate");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Settings Clone() => (Settings)MemberwiseClone();

    /// <summary>Loads the settings file, falling back to defaults when it is missing or invalid.</summary>
    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Settings();
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions);
            if (s == null)
                return new Settings();
            s.Validate();
            return s;
        }
        catch (Exception ex)
        {
            Log.Write($"Settings file could not be read, using defaults: {ex.Message}");
            return new Settings();
        }
    }

    /// <summary>Saves the settings file. Returns an error message, or null on success.</summary>
    public string? Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
            return null;
        }
        catch (Exception ex)
        {
            Log.Write($"Settings file could not be saved: {ex.Message}");
            return $"The settings could not be saved to {FilePath}. Check that the folder is writable. Details: {ex.Message}";
        }
    }

    /// <summary>Clamps every value into its valid range and replaces unknown enum values.</summary>
    public void Validate()
    {
        Speed = Math.Clamp(Speed, SliderMin, SliderMax);
        LineDensity = Math.Clamp(LineDensity, SliderMin, SliderMax);
        LineThickness = Math.Clamp(LineThickness, SliderMin, SliderMax);
        ColorChangeSpeed = Math.Clamp(ColorChangeSpeed, SliderMin, SliderMax);
        BloomIntensity = Math.Clamp(BloomIntensity, SliderMin, SliderMax);
        FilmGrain = Math.Clamp(FilmGrain, SliderMin, SliderMax);
        FilmGateWeave = Math.Clamp(FilmGateWeave, SliderMin, SliderMax);
        FilmLensSoftness = Math.Clamp(FilmLensSoftness, SliderMin, SliderMax);
        FilmHalation = Math.Clamp(FilmHalation, SliderMin, SliderMax);
        FilmFlicker = Math.Clamp(FilmFlicker, SliderMin, SliderMax);
        CenterLineWidth = Math.Clamp(CenterLineWidth, CenterLineMin, CenterLineMax);
        CustomFrameRate = Math.Clamp(CustomFrameRate, CustomFpsMin, CustomFpsMax);
        if (!Enum.IsDefined(WallMode)) WallMode = WallMode.Perspective;
        if (!Enum.IsDefined(CenterLineEdge)) CenterLineEdge = CenterLineEdge.Soft;
        if (!Enum.IsDefined(Direction)) Direction = ScrollDirection.TowardViewer;
        if (!Enum.IsDefined(Orientation)) Orientation = ScrollOrientation.Horizontal;
        if (!Enum.IsDefined(Screens)) Screens = ScreenSelection.AllScreens;
        if (!Enum.IsDefined(FrameRateLimit)) FrameRateLimit = FrameRateLimit.Automatic;
    }
}
