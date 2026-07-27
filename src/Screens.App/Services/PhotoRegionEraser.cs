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

    /// <summary>Flat-fill erase: samples the median color of a thin ring just outside the box and
    /// paints over the box with it. Good enough for solid/gradient backgrounds; leaves a visible
    /// patch on photographic ones (documented limitation, same tier as the phone-mockup corner clip).</summary>
    public static void Erase(SKBitmap bitmap, SKRectI box)
    {
        var ringColor = SampleRingColor(bitmap, box);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = ringColor, IsAntialias = false };
        canvas.DrawRect(new SKRect(box.Left, box.Top, box.Right, box.Bottom), paint);
    }

    private static SKColor SampleRingColor(SKBitmap bitmap, SKRectI box)
    {
        const int ring = 6;
        var samples = new List<SKColor>();
        void Sample(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < bitmap.Width && y < bitmap.Height)
                samples.Add(bitmap.GetPixel(x, y));
        }

        var stepX = Math.Max(1, box.Width / 20);
        for (var x = box.Left; x < box.Right; x += stepX)
        {
            Sample(x, box.Top - ring);
            Sample(x, box.Bottom + ring);
        }
        var stepY = Math.Max(1, box.Height / 10);
        for (var y = box.Top; y < box.Bottom; y += stepY)
        {
            Sample(box.Left - ring, y);
            Sample(box.Right + ring, y);
        }

        if (samples.Count == 0)
            return SKColors.White;

        var r = (byte)samples.Average(c => c.Red);
        var g = (byte)samples.Average(c => c.Green);
        var b = (byte)samples.Average(c => c.Blue);
        return new SKColor(r, g, b);
    }
}
