using System.IO;
using System.Text.Json;
using MarkdownVault.Models;

namespace MarkdownVault.Services;

/// <summary>Persists <see cref="AppSettings"/> to JSON in the user's AppData folder.</summary>
public class SettingsService
{
    private static readonly string DefaultPath = Path.Combine(AppPaths.Root, "settings.json");

    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true };

    /// <summary>
    /// Creates the service. <paramref name="settingsPath"/> overrides the default
    /// AppData location — used by tests to isolate persistence.
    /// </summary>
    public SettingsService(string? settingsPath = null) =>
        _settingsPath = settingsPath ?? DefaultPath;

    /// <summary>Loads settings from disk; returns defaults if file is missing or corrupt.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new AppSettings();
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>Saves settings to disk, creating the directory if needed.</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsService.Save failed: {ex.Message}");
        }
    }
}
