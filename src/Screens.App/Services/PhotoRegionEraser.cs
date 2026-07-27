using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace Screens.App.Services;

/// <summary>
/// Shared pixel-level helpers for turning a region of an existing photo into a "replace this
/// text" placeholder: approximating the original text's color, and erasing it with a flat fill
/// sampled from its immediate surroundings so the new value actually replaces the old one
/// instead of just being drawn on top of it. Used by both the OCR-based "Scan a picture"
/// pipeline (<see cref="OcrTemplateService"/>) and the manual "Build a template" flow
/// (<see cref="Screens.App.ViewModels.MainViewModel.SaveBuildTemplate"/>), so a hand-drawn text
/// region gets the exact same treatment as an automatically detected one.
/// </summary>
public static class PhotoRegionEraser
{
    /// <summary>Approximates the original text color from the darkest/lightest (highest-contrast)
    /// pixels inside the box — text is usually the highest-contrast content in its own box — and
    /// returns how much of the box those glyph pixels cover. That ink ratio isn't meaningful as
    /// an absolute number (it depends on the text itself, e.g. "iiii" vs "MMMM" at identical
    /// weight) but is meaningful relative to other regions in the same image: a heading set in a
    /// heavier weight than the surrounding body text reads as noticeably denser.</summary>
    public static (string Color, double InkRatio) SampleTextColorAndDensity(SKBitmap bitmap, SKRectI box)
    {
        var samples = new List<(byte r, byte g, byte b, double luma)>();
        var stepX = Math.Max(1, box.Width / 40);
        var stepY = Math.Max(1, box.Height / 20);
        for (var y = box.Top; y < box.Bottom; y += stepY)
        {
            for (var x = box.Left; x < box.Right; x += stepX)
            {
                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height) continue;
                var c = bitmap.GetPixel(x, y);
                var luma = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
                samples.Add((c.Red, c.Green, c.Blue, luma));
            }
        }
        if (samples.Count == 0)
            return ("#1A1028", 0.0);

        var avgLuma = samples.Average(s => s.luma);
        var darker = samples.Where(s => s.luma < avgLuma).ToList();
        var lighter = samples.Where(s => s.luma >= avgLuma).ToList();
        // The minority group is more likely to be the glyph strokes rather than the fill.
        var textPixels = darker.Count > 0 && darker.Count <= lighter.Count ? darker
            : lighter.Count > 0 && lighter.Count < darker.Count ? lighter
            : darker.Count > 0 ? darker : lighter;
        if (textPixels.Count == 0)
            return ("#1A1028", 0.0);

        var r = (byte)textPixels.Average(s => s.r);
        var g = (byte)textPixels.Average(s => s.g);
        var b = (byte)textPixels.Average(s => s.b);
        var inkRatio = (double)textPixels.Count / samples.Count;
        return ($"#{r:X2}{g:X2}{b:X2}", inkRatio);
    }

    /// <summary>Erases a region by filling it with a color derived from its immediate
    /// surroundings — a flat color if the surroundings are roughly uniform, or a linear gradient
    /// matched to the surroundings' own direction if they're not, which is the common case for
    /// this app's own bundled templates (most of their backgrounds are exactly a top-to-bottom or
    /// diagonal gradient — see generate_templates.py's vertical_gradient/diagonal_gradient — so a
    /// flat fill there used to leave a visible seam at the box edges). Still not real inpainting:
    /// a genuinely textured or photographic background will still show a visible patch, since a
    /// two-stop gradient can't approximate a texture — that limitation is unchanged.</summary>
    public static void Erase(SKBitmap bitmap, SKRectI box)
    {
        var top = SampleEdge(bitmap, box, top: true, left: false);
        var bottom = SampleEdge(bitmap, box, top: false, left: false);
        var left = SampleEdge(bitmap, box, top: false, left: true, vertical: false);
        var right = SampleEdge(bitmap, box, top: false, left: false, vertical: false);

        using var canvas = new SKCanvas(bitmap);
        var rect = new SKRect(box.Left, box.Top, box.Right, box.Bottom);

        var verticalDiff = ColorDistance(top, bottom);
        var horizontalDiff = ColorDistance(left, right);
        const double gradientThreshold = 10.0; // below this, the two edges are "the same color" (flat background)

        using var paint = new SKPaint { IsAntialias = false };
        if (verticalDiff < gradientThreshold && horizontalDiff < gradientThreshold)
        {
            // Flat background (or too subtle a gradient to bother matching): one averaged color,
            // the original behavior.
            paint.Color = Average(top, bottom, left, right);
        }
        else if (verticalDiff >= horizontalDiff)
        {
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(rect.MidX, rect.Top), new SKPoint(rect.MidX, rect.Bottom),
                new[] { top, bottom }, null, SKShaderTileMode.Clamp);
        }
        else
        {
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(rect.Left, rect.MidY), new SKPoint(rect.Right, rect.MidY),
                new[] { left, right }, null, SKShaderTileMode.Clamp);
        }

        canvas.DrawRect(rect, paint);
    }

    /// <summary>Median color along one edge of the box, sampled from a band just outside it (6-30px
    /// out, not a single thin line) so a small amount of noise/texture in the surrounding art gets
    /// smoothed out rather than picking one unlucky pixel.</summary>
    private static SKColor SampleEdge(SKBitmap bitmap, SKRectI box, bool top, bool left, bool vertical = true)
    {
        var samples = new List<SKColor>();
        void Sample(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < bitmap.Width && y < bitmap.Height)
                samples.Add(bitmap.GetPixel(x, y));
        }

        if (vertical)
        {
            var stepX = Math.Max(1, box.Width / 24);
            for (var x = box.Left; x < box.Right; x += stepX)
                for (var d = 6; d <= 30; d += 8)
                    Sample(x, top ? box.Top - d : box.Bottom + d);
        }
        else
        {
            var stepY = Math.Max(1, box.Height / 12);
            for (var y = box.Top; y < box.Bottom; y += stepY)
                for (var d = 6; d <= 30; d += 8)
                    Sample(left ? box.Left - d : box.Right + d, y);
        }

        if (samples.Count == 0)
            return SKColors.White;

        var r = (byte)samples.Average(c => c.Red);
        var g = (byte)samples.Average(c => c.Green);
        var b = (byte)samples.Average(c => c.Blue);
        return new SKColor(r, g, b);
    }

    private static double ColorDistance(SKColor a, SKColor b)
    {
        var dr = a.Red - b.Red;
        var dg = a.Green - b.Green;
        var db = a.Blue - b.Blue;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static SKColor Average(params SKColor[] colors) => new(
        (byte)colors.Average(c => c.Red),
        (byte)colors.Average(c => c.Green),
        (byte)colors.Average(c => c.Blue));
}
