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
    private SKTypeface? _bengali;

    private static readonly Dictionary<string, string> FileMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Inter|Regular|false"] = "Inter-Regular.ttf",
        ["Inter|Bold|false"] = "Inter-Bold.ttf",
        ["Playfair Display|Regular|false"] = "PlayfairDisplay-Regular.ttf",
        ["Playfair Display|Bold|false"] = "PlayfairDisplay-Bold.ttf",
        ["Cabin|Regular|false"] = "Cabin-Regular.ttf",
        ["Cabin|Bold|false"] = "Cabin-Bold.ttf",
    };

    /// <summary>True if any character in <paramref name="text"/> falls in the Bengali Unicode
    /// block (U+0980-U+09FF) — used to automatically switch to the Bengali typeface + HarfBuzz
    /// shaping for a field's text, regardless of what font family the template/field specifies,
    /// since none of the bundled Latin fonts carry Bengali glyphs at all.</summary>
    public static bool ContainsBengali(string text)
    {
        foreach (var ch in text)
            if (ch is >= 'ঀ' and <= '৿')
                return true;
        return false;
    }

    /// <summary>The bundled Bengali typeface (Noto Sans Bengali). There's only one weight bundled
    /// — a real bold instance would need instancing a variation axis on this variable font, which
    /// SkiaSharp 2.88 doesn't expose cleanly, so "Bold" Bengali text uses <c>SKPaint.FakeBoldText</c>
    /// (synthetic emboldening) on this same face instead of a second file.</summary>
    public SKTypeface ResolveBengali()
    {
        if (_bengali is not null)
            return _bengali;
        var path = Path.Combine(_fontsDir, "NotoSansBengali-Regular.ttf");
        _bengali = File.Exists(path) ? SKTypeface.FromFile(path) : Fallback();
        return _bengali;
    }

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
