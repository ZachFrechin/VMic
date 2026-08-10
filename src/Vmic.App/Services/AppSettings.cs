using System.IO;
using System.Text.Json;

namespace Vmic.App.Services;

/// <summary>
/// Persists the user's last role and device selections to
/// <c>%APPDATA%/Vmic/settings.json</c> so the app restores them on next launch.
/// </summary>
public sealed class AppSettings
{
    public string? LastRole { get; set; }
    public string? CaptureDeviceId { get; set; }
    public string? RenderDeviceId { get; set; }
    public string? LastHostIp { get; set; }
    public string? ClientName { get; set; }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vmic", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt/unreadable settings — fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Non-critical — settings just won't persist.
        }
    }
}
