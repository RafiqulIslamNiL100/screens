using System;
using System.IO;
using System.Text.Json;

namespace Screens.App.Services;

public sealed class AppConfig
{
    public string SupabaseUrl { get; init; } = "";
    public string SupabaseAnonKey { get; init; } = "";
    public string VersionJsonUrl { get; init; } = "";

    public bool IsSupabaseConfigured =>
        !string.IsNullOrWhiteSpace(SupabaseUrl) &&
        !SupabaseUrl.Contains("YOUR-PROJECT") &&
        !string.IsNullOrWhiteSpace(SupabaseAnonKey) &&
        !SupabaseAnonKey.Contains("YOUR-ANON-KEY");

    public static AppConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            return new AppConfig();

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var supabase = root.TryGetProperty("Supabase", out var s) ? s : default;
        var update = root.TryGetProperty("Update", out var u) ? u : default;

        return new AppConfig
        {
            SupabaseUrl = supabase.ValueKind == JsonValueKind.Object && supabase.TryGetProperty("Url", out var url) ? url.GetString() ?? "" : "",
            SupabaseAnonKey = supabase.ValueKind == JsonValueKind.Object && supabase.TryGetProperty("AnonKey", out var key) ? key.GetString() ?? "" : "",
            VersionJsonUrl = update.ValueKind == JsonValueKind.Object && update.TryGetProperty("VersionJsonUrl", out var vurl) ? vurl.GetString() ?? "" : "",
        };
    }
}
