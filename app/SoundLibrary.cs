using System.Text.Json;

namespace Ecoar;

/// <param name="Hotkey">Keys enum names joined by '+', e.g. "Ctrl+Alt+F1"; null = no hotkey.</param>
public sealed record SoundEntry(string Name, string Path, string? Hotkey)
{
    /// <summary>Seconds; null = from the beginning / to the end. The file itself is never changed.</summary>
    public double? TrimStart { get; init; }
    public double? TrimEnd { get; init; }
}

public sealed class AppConfig
{
    public List<SoundEntry> Sounds { get; set; } = new();
    public float MicVolume { get; set; } = 1f;
    public float HeadphoneVolume { get; set; } = 0.8f;
    public bool HearMyself { get; set; } = true;
    public string? StopHotkey { get; set; }
    public string? MicEndpointId { get; set; }
}

public static class SoundLibrary
{
    public static string DefaultPath { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ecoar", "config.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path)) return new AppConfig();
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options) ?? new AppConfig();
        }
        catch (JsonException)
        {
            // Keep the broken file for the user instead of silently losing their list.
            File.Move(path, $"{path}.bad-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config, string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, path, overwrite: true);   // atomic replace: a crash never leaves a half-written config
    }

    public static bool FileMissing(SoundEntry entry) => !File.Exists(entry.Path);
}
