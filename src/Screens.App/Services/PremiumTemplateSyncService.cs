using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Screens.App.Services;

/// <summary>A row of public.premium_templates_catalog (Supabase) — one manifest+image pair an
/// admin has published from inside the app (see PremiumAdminService.PublishTemplateAsync).
/// imageFile/manifestFile are filenames resolved against AppConfig.PremiumTemplatesStorageBaseUrl,
/// not full URLs, so the whole catalog can move without touching every entry. Version starts at 1
/// and only goes up when an admin republishes an edited copy of the same id — that's the only
/// signal EnsureDownloadedAsync has for "the local cached copy is stale, fetch it again."</summary>
public sealed class PremiumTemplateIndexEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("manifest_file")] public string ManifestFile { get; set; } = "";
    [JsonPropertyName("image_file")] public string ImageFile { get; set; } = "";
    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    /// <summary>Set by MainViewModel after a successful (or already up-to-date)
    /// PremiumTemplateSyncService.EnsureDownloadedAsync — not part of the index JSON itself, since
    /// it depends on this machine's local cache location. Null until then, which the gallery's
    /// thumbnail binding (via PathToBitmapConverter) already renders as "nothing" rather than an error.</summary>
    [JsonIgnore] public string? LocalImagePath { get; set; }
}

/// <summary>
/// Fetches the catalog of admin-published Premium Templates from Supabase (public.
/// premium_templates_catalog, read over plain HTTPS with just the anon key — no auth needed since
/// none of this is sensitive, same posture the old GitHub-hosted index.json had). Only ever
/// called once a user has unlocked access (see PremiumAccessService); templates are cached under
/// SettingsService.PremiumTemplatesDirectory, alongside a small "<id>.version" sidecar file
/// recording which catalog version is currently on disk — so re-opening the gallery re-downloads
/// only entries an admin has actually republished since, not everything every time.
/// </summary>
public sealed class PremiumTemplateSyncService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly AppConfig _config;
    private readonly SettingsService _settings;

    public PremiumTemplateSyncService(AppConfig config, SettingsService settings)
    {
        _config = config;
        _settings = settings;
        if (_config.IsSupabaseConfigured)
            _http.DefaultRequestHeaders.Add("apikey", _config.SupabaseAnonKey);
    }

    /// <summary>Fetches the current catalog listing. Never throws — an empty list on any failure
    /// (no network, bad JSON, 404) is the correct "nothing available right now" state, same as
    /// UpdateService.CheckAsync's silent-failure convention.</summary>
    public async Task<IReadOnlyList<PremiumTemplateIndexEntry>> FetchIndexAsync()
    {
        if (!_config.IsSupabaseConfigured)
            return Array.Empty<PremiumTemplateIndexEntry>();

        try
        {
            var url = _config.PremiumTemplatesCatalogUrl + "?select=id,name,category,manifest_file,image_file,version&order=name.asc";
            var entries = await _http.GetFromJsonAsync<List<PremiumTemplateIndexEntry>>(url);
            return entries ?? new List<PremiumTemplateIndexEntry>();
        }
        catch
        {
            return Array.Empty<PremiumTemplateIndexEntry>();
        }
    }

    private string VersionSidecarPath(PremiumTemplateIndexEntry entry) =>
        Path.Combine(_settings.PremiumTemplatesDirectory, $"{entry.Id}.version");

    /// <summary>The local cached image path for this entry, whether or not it's been downloaded
    /// yet — safe to bind directly in a thumbnail <c>Image</c> control alongside <see cref="PathToBitmapConverter"/>,
    /// which already tolerates a missing/invalid path by rendering nothing.</summary>
    public string LocalImagePath(PremiumTemplateIndexEntry entry) =>
        Path.Combine(_settings.PremiumTemplatesDirectory, Path.GetFileName(entry.ImageFile));

    /// <summary>True if this entry is cached locally *and* at least as new as what the index
    /// currently lists — false either when never downloaded, or when the admin has shipped a
    /// newer version since the last download.</summary>
    public bool IsUpToDate(PremiumTemplateIndexEntry entry)
    {
        var manifestPath = Path.Combine(_settings.PremiumTemplatesDirectory, $"{entry.Id}.json");
        var imagePath = LocalImagePath(entry);
        if (!File.Exists(manifestPath) || !File.Exists(imagePath))
            return false;

        var versionPath = VersionSidecarPath(entry);
        if (!File.Exists(versionPath))
            return false; // downloaded before version tracking existed — treat as stale once, harmlessly re-fetched.
        return int.TryParse(File.ReadAllText(versionPath).Trim(), out var cachedVersion) && cachedVersion >= entry.Version;
    }

    /// <summary>Downloads and caches one entry's manifest+image if not already present, or if the
    /// admin has re-shipped a newer version since it was last downloaded. Returns false (without
    /// throwing) on any failure, so the caller can show "couldn't download this template" without
    /// the whole gallery breaking.</summary>
    public async Task<bool> EnsureDownloadedAsync(PremiumTemplateIndexEntry entry)
    {
        if (IsUpToDate(entry))
            return true;

        try
        {
            var baseUrl = _config.PremiumTemplatesStorageBaseUrl;
            var manifestBytes = await _http.GetByteArrayAsync(baseUrl + entry.ManifestFile);
            var imageBytes = await _http.GetByteArrayAsync(baseUrl + entry.ImageFile);

            Directory.CreateDirectory(_settings.PremiumTemplatesDirectory);
            // The manifest's own "image" field must point at whatever filename we actually saved
            // it under locally, which is just entry.ImageFile's basename — same convention every
            // other template folder already uses (manifest + image side by side, image referenced
            // by relative filename).
            await File.WriteAllBytesAsync(LocalImagePath(entry), imageBytes);
            await File.WriteAllBytesAsync(Path.Combine(_settings.PremiumTemplatesDirectory, $"{entry.Id}.json"), manifestBytes);
            await File.WriteAllTextAsync(VersionSidecarPath(entry), entry.Version.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }
}
