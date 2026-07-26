using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace Screens.App.Services;

public enum ExportFormat { Png, Jpg, Pdf }

/// <summary>
/// Loads bundled OFL/Apache-licensed fonts once and resolves a manifest's
/// font family+weight+italic to a concrete SKTypeface, falling back to a
/// bundled sans-serif (never the OS font store — spec section 6).
/// </summary>
public sealed class FontRegistry
{
    private readonly Dictionary<string, SKTypeface> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _fontsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
    private SKTypeface? _fallback;

    private static readonly Dictionary<string, string> FileMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Inter|Regular|false"] = "Inter-Regular.ttf",
        ["Inter|Bold|false"] = "Inter-Bold.ttf",
        ["Playfair Display|Regular|false"] = "PlayfairDisplay-Regular.ttf",
        ["Playfair Display|Bold|false"] = "PlayfairDisplay-Bold.ttf",
        ["Cabin|Regular|false"] = "Cabin-Regular.ttf",
        ["Cabin|Bold|false"] = "Cabin-Bold.ttf",
    };

    public SKTypeface Resolve(string family, string weight, bool italic)
    {
        var key = $"{family}|{weight}|{italic}";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        if (FileMap.TryGetValue(key, out var fileName))
        {
            var path = Path.Combine(_fontsDir, fileName);
            if (File.Exists(path))
            {
                var typeface = SKTypeface.FromFile(path);
                if (typeface is not null)
                {
                    _cache[key] = typeface;
                    return typeface;
                }
            }
        }

        return Fallback();
    }

    private SKTypeface Fallback()
    {
        if (_fallback is not null)
            return _fallback;
        var path = Path.Combine(_fontsDir, "Inter-Regular.ttf");
        _fallback = File.Exists(path) ? SKTypeface.FromFile(path) : SKTypeface.Default;
        return _fallback;
    }
}
