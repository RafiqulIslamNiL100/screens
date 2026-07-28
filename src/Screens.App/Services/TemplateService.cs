using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Screens.App.Models;

namespace Screens.App.Services;

public sealed record TemplateLoadWarning(string FileName, string Message);

/// <summary>
/// Loads bundled templates (shipped in Assets/Templates, copied next to the
/// exe) plus any dropped into %APPDATA%\Screens\Templates. A malformed
/// manifest is skipped with a warning, never a crash (spec section 6).
/// </summary>
public sealed class TemplateService
{
    private readonly SettingsService _settings;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public List<TemplateLoadWarning> Warnings { get; } = new();

    public TemplateService(SettingsService settings) => _settings = settings;

    public IReadOnlyList<TemplateManifest> LoadAll()
    {
        Warnings.Clear();
        var result = new List<TemplateManifest>();

        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "Templates");
        LoadFrom(bundled, result);
        LoadFrom(_settings.TemplatesDirectory, result);
        LoadFrom(_settings.PremiumTemplatesDirectory, result);

        return result.OrderBy(t => t.Category).ThenBy(t => t.Name).ToList();
    }

    private void LoadFrom(string directory, List<TemplateManifest> result)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var jsonPath in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<TemplateManifest>(File.ReadAllText(jsonPath), JsonOpts);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Image))
                {
                    Warnings.Add(new TemplateLoadWarning(Path.GetFileName(jsonPath), "Manifest is missing required fields (id/image)."));
                    continue;
                }

                var imagePath = Path.Combine(directory, manifest.Image);
                if (!File.Exists(imagePath))
                {
                    Warnings.Add(new TemplateLoadWarning(Path.GetFileName(jsonPath), $"Image '{manifest.Image}' not found next to manifest."));
                    continue;
                }

                manifest.SourceDirectory = directory;
                manifest.IsCustom = directory == _settings.TemplatesDirectory;
                manifest.IsPremium = directory == _settings.PremiumTemplatesDirectory;
                if (manifest.IsPremium)
                {
                    // Force-locked regardless of what the manifest itself says — the point of a
                    // Premium Template is that the admin's font/size choices are what ships.
                    foreach (var field in manifest.Fields)
                    {
                        field.UserEditableColor = false;
                        field.UserEditableSize = false;
                        field.UserEditableFont = false;
                    }
                }
                result.Add(manifest);
            }
            catch (JsonException ex)
            {
                Warnings.Add(new TemplateLoadWarning(Path.GetFileName(jsonPath), $"Invalid JSON: {ex.Message}"));
            }
            catch (Exception ex)
            {
                Warnings.Add(new TemplateLoadWarning(Path.GetFileName(jsonPath), ex.Message));
            }
        }
    }
}
