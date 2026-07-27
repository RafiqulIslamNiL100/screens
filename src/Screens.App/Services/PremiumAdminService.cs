using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Screens.App.Models;

namespace Screens.App.Services;

/// <summary>
/// Lets a signed-in admin (a row in public.admins — see db/schema.sql) publish a Premium Template
/// directly from inside the app: upload the manifest+image to the "premium-templates" Storage
/// bucket, then upsert a row in public.premium_templates_catalog, both under the admin's own
/// authenticated session (RLS's is_admin() check is what actually enforces this — nothing here is
/// a substitute for that). This replaces the old flow of exporting a package and hand-committing
/// it to the screens-fnl-app GitHub repo; every existing user's app keeps working exactly the same
/// way either way, since PremiumTemplateSyncService just reads whatever's in the catalog/bucket.
/// </summary>
public sealed class PremiumAdminService
{
    private readonly AuthService _auth;
    private readonly AppConfig _config;

    public bool IsAdmin { get; private set; }

    public PremiumAdminService(AuthService auth, AppConfig config)
    {
        _auth = auth;
        _config = config;
    }

    /// <summary>Checks (and caches on <see cref="IsAdmin"/>) whether the current session belongs
    /// to an admin. Never throws — a network hiccup here must just mean "don't show the admin
    /// button this time," not crash startup.</summary>
    public async Task<bool> CheckIsAdminAsync()
    {
        IsAdmin = false;
        if (!_config.IsSupabaseConfigured || _auth.CurrentSession is null)
            return false;

        try
        {
            var url = $"{_config.SupabaseUrl.TrimEnd('/')}/rest/v1/admins?user_id=eq.{_auth.CurrentSession.UserId}&select=user_id";
            var response = await _auth.Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return false;

            var rows = await response.Content.ReadFromJsonAsync<AdminRow[]>(JsonOpts);
            IsAdmin = rows is { Length: > 0 };
            return IsAdmin;
        }
        catch
        {
            return false;
        }
    }

    public sealed class PublishResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public int PublishedVersion { get; init; }

        public static PublishResult Ok(int version) => new() { Success = true, PublishedVersion = version };
        public static PublishResult Fail(string error) => new() { Success = false, Error = error };
    }

    /// <summary>Publishes (or republishes) one template: uploads its manifest JSON and background
    /// image, unmodified, to the Storage bucket, then upserts its catalog row. If the same id was
    /// published before, the version is bumped automatically (existing + 1) so every user's
    /// already-cached copy is detected as stale by PremiumTemplateSyncService.IsUpToDate the next
    /// time they open the gallery — the admin never has to remember to bump anything by hand.</summary>
    public async Task<PublishResult> PublishTemplateAsync(TemplateManifest manifest, string sourceImagePath, string name, string category)
    {
        if (!_config.IsSupabaseConfigured || _auth.CurrentSession is null)
            return PublishResult.Fail("Not signed in.");
        if (!File.Exists(sourceImagePath))
            return PublishResult.Fail("Template image file not found.");

        try
        {
            var id = manifest.Id;
            var imageFile = $"{id}{Path.GetExtension(sourceImagePath)}";
            var manifestFile = $"{id}.json";

            var existingVersion = await FetchExistingVersionAsync(id);
            var version = existingVersion + 1;

            var imageBytes = await File.ReadAllBytesAsync(sourceImagePath);
            var uploadImage = await UploadAsync(imageFile, imageBytes, GuessContentType(imageFile));
            if (!uploadImage)
                return PublishResult.Fail("Couldn't upload the template image — check your connection and try again.");

            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            var uploadManifest = await UploadAsync(manifestFile, manifestBytes, "application/json");
            if (!uploadManifest)
                return PublishResult.Fail("Couldn't upload the template manifest — check your connection and try again.");

            var catalogOk = await UpsertCatalogRowAsync(id, name, category, manifestFile, imageFile, version);
            if (!catalogOk)
                return PublishResult.Fail("Files uploaded, but the catalog entry failed to save. Try publishing again.");

            return PublishResult.Ok(version);
        }
        catch (Exception ex)
        {
            return PublishResult.Fail($"Publish failed: {ex.Message}");
        }
    }

    private async Task<int> FetchExistingVersionAsync(string id)
    {
        try
        {
            var url = $"{_config.PremiumTemplatesCatalogUrl}?id=eq.{Uri.EscapeDataString(id)}&select=version";
            var response = await _auth.Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return 0;
            var rows = await response.Content.ReadFromJsonAsync<CatalogVersionRow[]>(JsonOpts);
            return rows is { Length: > 0 } ? rows[0].Version : 0;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<bool> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        var url = _config.PremiumTemplatesStorageUploadBaseUrl + fileName;
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        // Storage's upsert semantics: without this header, re-publishing the same filename (the
        // whole point of republishing an edited template under the same id) would 409 instead of
        // overwriting.
        request.Headers.Add("x-upsert", "true");

        var response = await _auth.Http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private async Task<bool> UpsertCatalogRowAsync(string id, string name, string category, string manifestFile, string imageFile, int version)
    {
        var url = _config.PremiumTemplatesCatalogUrl + "?on_conflict=id";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                id,
                name,
                category,
                manifest_file = manifestFile,
                image_file = imageFile,
                version,
                updated_at = DateTimeOffset.UtcNow,
            }),
        };
        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

        var response = await _auth.Http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => "image/png",
    };

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class AdminRow
    {
        [JsonPropertyName("user_id")] public string? UserId { get; set; }
    }

    private sealed class CatalogVersionRow
    {
        [JsonPropertyName("version")] public int Version { get; set; }
    }
}
