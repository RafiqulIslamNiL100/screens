using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using Screens.App.Models;
using SkiaSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Screens.App.Services;

/// <summary>
/// "Scan a picture" — turns an arbitrary uploaded image into an editable
/// template by detecting text regions with Windows' built-in OCR engine
/// (free, fully offline, no extra dependency), erasing the recognized text
/// from the background, and generating one field per detected line sized to
/// its bounding box.
///
/// Two things this deliberately does NOT do, by nature of the approach:
///   - font *identification*. There's no offline, license-free way to match
///     pixels back to a specific font family, so every field defaults to the
///     bundled Inter typeface sized to the detected line height, with
///     UserEditableColor/Size left on so the user can nudge it to match.
///   - real inpainting. The erase step fills each text box with a flat color
///     sampled from its immediate surroundings, which reads cleanly on
///     solid/gradient backgrounds (certificates, posters) but can leave a
///     visible patch on photographic backgrounds.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OcrTemplateService
{
    private readonly SettingsService _settings;

    public OcrTemplateService(SettingsService settings) => _settings = settings;

    public sealed class ScanException : Exception
    {
        public ScanException(string message) : base(message) { }
    }

    /// <summary>Runs OCR on <paramref name="sourceImagePath"/>, erases the detected text from the
    /// background, and writes a new custom template. Returns the new template's id.</summary>
    public async Task<string> ScanAsync(string sourceImagePath)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? TryFallbackEnglish()
            ?? throw new ScanException("No OCR language is installed on this Windows install. Install a language pack in Windows Settings > Time & Language > Language, then try again.");

        SoftwareBitmap softwareBitmap;
        using (var stream = new FileStream(sourceImagePath, FileMode.Open, FileAccess.Read))
        using (var randomAccessStream = stream.AsRandomAccessStream())
        {
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            var raw = await decoder.GetSoftwareBitmapAsync();
            softwareBitmap = SoftwareBitmap.Convert(raw, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            raw.Dispose();
        }

        OcrResult result;
        using (softwareBitmap)
            result = await engine.RecognizeAsync(softwareBitmap);

        if (result.Lines.Count == 0)
            throw new ScanException("No text was detected in this picture.");

        using var background = SKBitmap.Decode(sourceImagePath)
            ?? throw new ScanException("Could not read this picture.");

        var fields = new List<TemplateField>();
        var index = 0;
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text))
                continue;

            var box = UnionRect(line);
            var padded = Pad(box, background.Width, background.Height);

            var color = SampleTextColor(background, box);
            EraseRegion(background, padded);

            index++;
            fields.Add(new TemplateField
            {
                Id = $"scanned{index}",
                Type = "text",
                Label = Truncate(line.Text, 40),
                Default = line.Text,
                Placeholder = line.Text,
                Box = new FieldBox { X = padded.Left, Y = padded.Top, Width = padded.Width, Height = padded.Height },
                Align = "left",
                VerticalAlign = "middle",
                Font = new FieldFont { Family = "Inter", Size = Math.Max(8, box.Height * 0.72), Weight = "Regular" },
                Color = color,
                AutoShrink = true,
                UserEditableColor = true,
                UserEditableSize = true,
            });
        }

        if (fields.Count == 0)
            throw new ScanException("No text was detected in this picture.");

        var newId = $"scan-{DateTime.Now:yyyyMMddHHmmss}";
        var destDir = _settings.TemplatesDirectory;
        Directory.CreateDirectory(destDir);

        var imageName = $"{newId}.png";
        using (var fs = File.Create(Path.Combine(destDir, imageName)))
        using (var image = SKImage.FromBitmap(background))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            data.SaveTo(fs);

        var manifest = new TemplateManifest
        {
            Id = newId,
            Name = $"Scanned {DateTime.Now:MMM d, HH:mm}",
            Category = "Scanned",
            Image = imageName,
            Canvas = new CanvasSize { Width = background.Width, Height = background.Height },
            Fields = fields,
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(destDir, $"{newId}.json"), json);

        return newId;
    }

    private static OcrEngine? TryFallbackEnglish()
    {
        try { return OcrEngine.TryCreateFromLanguage(new Language("en")); }
        catch { return null; }
    }

    private static SKRectI UnionRect(OcrLine line)
    {
        var rect = line.Words[0].BoundingRect;
        double left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;
        foreach (var word in line.Words.Skip(1))
        {
            var r = word.BoundingRect;
            left = Math.Min(left, r.Left);
            top = Math.Min(top, r.Top);
            right = Math.Max(right, r.Right);
            bottom = Math.Max(bottom, r.Bottom);
        }
        return new SKRectI((int)left, (int)top, (int)right, (int)bottom);
    }

    /// <summary>Grows the detected box a bit so text isn't flush against its field's edges — the
    /// same convention the bundled templates use — and clamps to the image bounds.</summary>
    private static SKRectI Pad(SKRectI box, int imageWidth, int imageHeight)
    {
        var padX = Math.Max(4, box.Height * 0.2);
        var padY = Math.Max(4, box.Height * 0.25);
        var left = Math.Max(0, box.Left - padX);
        var top = Math.Max(0, box.Top - padY);
        var right = Math.Min(imageWidth, box.Right + padX);
        var bottom = Math.Min(imageHeight, box.Bottom + padY);
        return new SKRectI((int)left, (int)top, (int)right, (int)bottom);
    }

    /// <summary>Approximates the original text color from the darkest (highest-contrast) pixels
    /// inside the detected box — text is usually the highest-contrast content in its own box.</summary>
    private static string SampleTextColor(SKBitmap bitmap, SKRectI box)
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
            return "#1A1028";

        var avgLuma = samples.Average(s => s.luma);
        var darker = samples.Where(s => s.luma < avgLuma).ToList();
        var lighter = samples.Where(s => s.luma >= avgLuma).ToList();
        // The minority group is more likely to be the glyph strokes rather than the fill.
        var textPixels = darker.Count > 0 && darker.Count <= lighter.Count ? darker
            : lighter.Count > 0 && lighter.Count < darker.Count ? lighter
            : darker.Count > 0 ? darker : lighter;
        if (textPixels.Count == 0)
            return "#1A1028";

        var r = (byte)textPixels.Average(s => s.r);
        var g = (byte)textPixels.Average(s => s.g);
        var b = (byte)textPixels.Average(s => s.b);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>Flat-fill erase: samples the median color of a thin ring just outside the box and
    /// paints over the box with it. Good enough for solid/gradient backgrounds; leaves a visible
    /// patch on photographic ones (documented limitation, same tier as the phone-mockup corner clip).</summary>
    private static void EraseRegion(SKBitmap bitmap, SKRectI box)
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

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
