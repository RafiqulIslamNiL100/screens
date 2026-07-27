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
///     bundled Inter typeface sized to the detected line height (bold vs.
///     regular weight is approximated per line — see the ink-density comment
///     below — but the family itself is never identified), with
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

        // Two passes: first collect every line's raw (unpadded) box, filtering out fragments too
        // small to sensibly edit (stray glyphs/icons OCR sometimes latches onto). Then pad each
        // box, but clamp the padding against every *other* raw box so two lines that sit close
        // together (a header + its subtitle, tabs in a strip, a label + its button) never grow
        // into each other — the earlier fixed-padding version did exactly that, producing
        // overlapping, illegibly-collided text once real values were rendered in.
        const int minLineHeight = 8;
        const int minLineWidth = 10;

        var lines = new List<OcrLine>();
        var rawBoxes = new List<SKRectI>();
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text))
                continue;
            var box = UnionRect(line);
            if (box.Height < minLineHeight || box.Width < minLineWidth)
                continue;
            lines.Add(line);
            rawBoxes.Add(box);
        }

        // Sample color + ink density for every line before building fields. Ink density (fraction
        // of the box that's glyph-colored rather than background) is a rough proxy for stroke
        // weight — not reliable as an absolute number (it depends on the text itself, e.g. "iiii"
        // vs "MMMM"), but reliable *relative to the rest of the same image*: a heading rendered in
        // a bold weight reads as noticeably denser than the body text around it in the same font.
        // Lines whose density is well above the image's median are treated as bold.
        var textInfo = new (string Color, double InkRatio)[lines.Count];
        for (var i = 0; i < lines.Count; i++)
            textInfo[i] = SampleTextColorAndDensity(background, rawBoxes[i]);

        var medianInk = textInfo.Length > 0
            ? textInfo.Select(t => t.InkRatio).OrderBy(v => v).ElementAt(textInfo.Length / 2)
            : 0.0;

        var fields = new List<TemplateField>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var box = rawBoxes[i];
            var padded = ClampedPad(box, rawBoxes, i, background.Width, background.Height);

            EraseRegion(background, padded);

            var isBold = medianInk > 0 && textInfo[i].InkRatio > medianInk * 1.3;

            fields.Add(new TemplateField
            {
                Id = $"scanned{i + 1}",
                Type = "text",
                Label = Truncate(line.Text, 40),
                Default = line.Text,
                Placeholder = line.Text,
                Box = new FieldBox { X = padded.Left, Y = padded.Top, Width = padded.Width, Height = padded.Height },
                Align = "left",
                VerticalAlign = "middle",
                Font = new FieldFont { Family = "Inter", Size = Math.Max(8, box.Height * 0.72), Weight = isBold ? "Bold" : "Regular" },
                Color = textInfo[i].Color,
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
    /// same convention the bundled templates use — but only as far as the nearest neighboring
    /// line allows, so two close-together lines never pad into each other. A neighbor only
    /// constrains a side if it actually sits on that side (shares column range for top/bottom,
    /// shares row range for left/right); each side gets at most half the gap to that neighbor.</summary>
    private static SKRectI ClampedPad(SKRectI box, IReadOnlyList<SKRectI> all, int selfIndex, int imageWidth, int imageHeight)
    {
        double maxLeft = Math.Max(3, box.Height * 0.2);
        double maxRight = maxLeft;
        double maxTop = Math.Max(3, box.Height * 0.25);
        double maxBottom = maxTop;

        for (var i = 0; i < all.Count; i++)
        {
            if (i == selfIndex) continue;
            var other = all[i];

            var xOverlap = Math.Min(box.Right, other.Right) - Math.Max(box.Left, other.Left);
            if (xOverlap > 0)
            {
                if (other.Bottom <= box.Top)
                    maxTop = Math.Min(maxTop, (box.Top - other.Bottom) / 2.0);
                if (other.Top >= box.Bottom)
                    maxBottom = Math.Min(maxBottom, (other.Top - box.Bottom) / 2.0);
            }

            var yOverlap = Math.Min(box.Bottom, other.Bottom) - Math.Max(box.Top, other.Top);
            if (yOverlap > 0)
            {
                if (other.Right <= box.Left)
                    maxLeft = Math.Min(maxLeft, (box.Left - other.Right) / 2.0);
                if (other.Left >= box.Right)
                    maxRight = Math.Min(maxRight, (other.Left - box.Right) / 2.0);
            }
        }

        maxLeft = Math.Max(0, maxLeft);
        maxRight = Math.Max(0, maxRight);
        maxTop = Math.Max(0, maxTop);
        maxBottom = Math.Max(0, maxBottom);

        var left = Math.Max(0, box.Left - maxLeft);
        var top = Math.Max(0, box.Top - maxTop);
        var right = Math.Min(imageWidth, box.Right + maxRight);
        var bottom = Math.Min(imageHeight, box.Bottom + maxBottom);
        return new SKRectI((int)left, (int)top, (int)right, (int)bottom);
    }

    /// <summary>Approximates the original text color from the darkest/lightest (highest-contrast)
    /// pixels inside the detected box — text is usually the highest-contrast content in its own
    /// box — and returns how much of the box those glyph pixels cover (see the ink-density comment
    /// at the call site for what that's used for).</summary>
    private static (string Color, double InkRatio) SampleTextColorAndDensity(SKBitmap bitmap, SKRectI box)
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
