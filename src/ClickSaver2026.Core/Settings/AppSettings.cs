using System.Text.Json;

namespace ClickSaver2026.Core.Settings;

public sealed class AppSettings
{
    /// <summary>The Anarchy Online install to read item names and icons from.</summary>
    public string? ClientFolder { get; set; }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickSaver2026", "settings.json");

    private static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };

    /// <summary>The saved settings, or defaults when there are none or they cannot be read.</summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }
}
