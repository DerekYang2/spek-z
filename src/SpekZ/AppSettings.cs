using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spek.Net;

namespace SpekZ;

internal static class AppInfo
{
    public const string Name = "Spek-Z";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
}

internal sealed class AppSettings
{
    public string? FfmpegPath { get; set; }
    public Palette Palette { get; set; } = Palettes.Default;
    public WindowFunction WindowFunction { get; set; } = WindowFunction.Hann;
    public int FftBits { get; set; } = SpekDefaults.FftBits;
    public int LowerDb { get; set; } = SpekDefaults.LowerDb;
    public int UpperDb { get; set; } = SpekDefaults.UpperDb;
    public bool ShowFullPath { get; set; } = true;
    public bool ShowDetailedDescription { get; set; }
    public ReferenceLines ReferenceLines { get; set; } = ReferenceLines.None;
    public bool ShowSpectrum { get; set; }
    public bool ShowCutoffMarker { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spek-Z", "settings.json");

    private static string LegacyFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentSpek", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            string path = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            if (!File.Exists(path)) return new AppSettings();
            var settings = JsonSerializer.Deserialize(File.ReadAllText(path), AppSettingsContext.Default.AppSettings) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, AppSettingsContext.Default.AppSettings));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ffmpeg from the settings, then next to the app, then on PATH.
    public static string? FindFfmpeg(AppSettings settings) => Ffmpeg.Find(
    [
        settings.FfmpegPath,
        Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
        Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
    ]);

    private void Normalize()
    {
        FftBits = Math.Clamp(FftBits, SpekDefaults.MinFftBits, SpekDefaults.MaxFftBits);
        UpperDb = Math.Clamp(UpperDb, SpekDefaults.MinDb + 1, SpekDefaults.MaxDb);
        LowerDb = Math.Clamp(LowerDb, SpekDefaults.MinDb, UpperDb - 1);
        if (!Enum.IsDefined(Palette)) Palette = Palettes.Default;
        if (!Enum.IsDefined(WindowFunction)) WindowFunction = WindowFunction.Hann;
        if (!Enum.IsDefined(ReferenceLines)) ReferenceLines = ReferenceLines.None;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsContext : JsonSerializerContext;
