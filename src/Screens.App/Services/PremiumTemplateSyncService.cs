using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Screens.App.Services;

/// <summary>An entry in premium-templates/index.json (distribution repo) — one manifest+image
/// pair the admin has shipped. imageFile/manifestFile are filenames resolved against
/// AppConfig.PremiumTemplatesBaseUrl, not full URLs, so the whole catalog can move without
/// touching every entry.</summary>
public sealed class PremiumTemplateIndexEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("manifestFile")] public string ManifestFile { get; set; } = "";
    [JsonPropertyName("imageFile")] public string ImageFile { get; set; } = "";
}

/// <summary>
/// Fetches the catalog of admin-shipped Premium Templates the same way UpdateService checks for
/// app updates — a JSON index hosted in the distribution repo, read over plain HTTPS, no auth
/// needed since none of this is sensitive (it's the same public content as the installer). Only
/// ever called once a user has unlocked access (see PremiumAccessService); the index and any
/// template files are downloaded on demand and cached under SettingsService.PremiumTemplatesDirectory,
/// so a re-open of the gallery doesn't re-download templates already fetched.
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
    }

    /// <summary>Fetches the current catalog listing. Never throws — an empty list on any failure
    /// (no network, bad JSON, 404) is the correct "nothing available right now" state, same as
    /// UpdateService.CheckAsync's silent-failure convention.</summary>
    public async Task<IReadOnlyList<PremiumTemplateIndexEntry>> FetchIndexAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.PremiumTemplatesIndexUrl))
            return Array.Empty<PremiumTemplateIndexEntry>();

        try
        {
            var entries = await _http.GetFromJsonAsync<List<PremiumTemplateIndexEntry>>(_config.PremiumTemplatesIndexUrl);
            return entries ?? new List<PremiumTemplateIndexEntry>();
        }
        catch
        {
            return Array.Empty<PremiumTemplateIndexEntry>();
        }
    }

    /// <summary>True if this entry's manifest+image are already cached locally (so selecting it
    /// doesn't need a fresh download).</summary>
    public bool IsCached(PremiumTemplateIndexEntry entry) =>
        File.Exists(Path.Combine(_settings.PremiumTemplatesDirectory, $"{entry.Id}.json"));

    /// <summary>Downloads and caches one entry's manifest+image if not already present. Returns
    /// false (without throwing) on any failure, so the caller can show "couldn't download this
    /// template" without the whole gallery breaking.</summary>
    public async Task<bool> EnsureDownloadedAsync(PremiumTemplateIndexEntry entry)
    {
        if (IsCached(entry))
            return true;

        try
        {
            var baseUrl = _config.PremiumTemplatesBaseUrl.TrimEnd('/') + "/";
            var manifestBytes = await _http.GetByteArrayAsync(baseUrl + entry.ManifestFile);
            var imageBytes = await _http.GetByteArrayAsync(baseUrl + entry.ImageFile);

            Directory.CreateDirectory(_settings.PremiumTemplatesDirectory);
            var imageName = Path.GetFileName(entry.ImageFile);
            await File.WriteAllBytesAsync(Path.Combine(_settings.PremiumTemplatesDirectory, imageName), imageBytes);
            // The manifest's own "image" field must point at whatever filename we actually saved
            // it under locally, which is just entry.ImageFile's basename — same convention every
            // other template folder already uses (manifest + image side by side, image referenced
            // by relative filename).
            await File.WriteAllBytesAsync(Path.Combine(_settings.PremiumTemplatesDirectory, $"{entry.Id}.json"), manifestBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
