using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Screens.App.Models;

namespace Screens.App.Services;

public enum ThemePreference { Light, Dark, System }

public sealed class ExportPreset
{
    public string Name { get; set; } = "";
    public string Format { get; set; } = "png"; // png | jpg | pdf
    public string? Folder { get; set; } // null = default Pictures\Screens
}

public sealed class AppSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public AppLanguage Language { get; set; } = AppLanguage.English;
    public string? LastTemplateId { get; set; }
    /// <summary>Most-recently-used template ids, newest first, capped at 8.</summary>
    public List<string> RecentTemplateIds { get; set; } = new();
    /// <summary>Per-template field values, so in-progress edits survive an app restart.</summary>
    public Dictionary<string, Dictionary<string, string>> Drafts { get; set; } = new();
    public List<ExportPreset> ExportPresets { get; set; } = new();
    public bool WatermarkEnabled { get; set; }
    public bool SnapToGridEnabled { get; set; } = true;
}

/// <summary>
/// Owns everything under %APPDATA%\Screens: session (DPAPI-protected),
/// license cache, and user settings. The uninstaller preserves this folder.
/// </summary>
public sealed class SettingsService
{
    private readonly string _root;
    private readonly string _sessionPath;
    private readonly string _licensePath;
    private readonly string _settingsPath;

    public SettingsService()
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screens");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "Templates"));
        _sessionPath = Path.Combine(_root, "session.dat");
        _licensePath = Path.Combine(_root, "license.json");
        _settingsPath = Path.Combine(_root, "settings.json");
    }

    public string TemplatesDirectory => Path.Combine(_root, "Templates");
    public string DefaultExportDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screens");

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings) =>
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings));

    public async Task SaveSessionAsync(Session session)
    {
        var json = JsonSerializer.Serialize(session);
        var bytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectIfWindows(bytes);
        await File.WriteAllBytesAsync(_sessionPath, protectedBytes);
    }

    public async Task<Session?> LoadSessionAsync()
    {
        if (!File.Exists(_sessionPath))
            return null;
        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(_sessionPath);
            var bytes = UnprotectIfWindows(protectedBytes);
            return JsonSerializer.Deserialize<Session>(Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return null;
        }
    }

    public Task ClearSessionAsync()
    {
        if (File.Exists(_sessionPath))
            File.Delete(_sessionPath);
        return Task.CompletedTask;
    }

    public async Task SaveLicenseStateAsync(LicenseState state) =>
        await File.WriteAllTextAsync(_licensePath, JsonSerializer.Serialize(state));

    public async Task<LicenseState?> LoadLicenseStateAsync()
    {
        if (!File.Exists(_licensePath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<LicenseState>(await File.ReadAllTextAsync(_licensePath));
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] data) =>
        ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] data) =>
        ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);

    private static byte[] ProtectIfWindows(byte[] data) =>
        OperatingSystem.IsWindows() ? ProtectWindows(data) : data;

    private static byte[] UnprotectIfWindows(byte[] data) =>
        OperatingSystem.IsWindows() ? UnprotectWindows(data) : data;
}
