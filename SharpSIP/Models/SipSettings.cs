using System.IO;
using System.Text.Json;

namespace SharpSIP.Models;

/// <summary>
/// Holds the SIP account configuration entered by the user.
/// </summary>
public class SipSettings
{
    public string SipServer { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // --- persistence ----------------------------------------------------------

    private static readonly string SettingsPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SharpSIP",
            "settings.json");

    /// <summary>Loads settings from disk, or returns defaults if not found.</summary>
    public static SipSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<SipSettings>(json) ?? new SipSettings();
            }
        }
        catch
        {
            // If the file is corrupt, just return defaults.
        }

        return new SipSettings();
    }

    /// <summary>Saves settings to disk.</summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Non-fatal; settings simply won't be persisted.
        }
    }
}
