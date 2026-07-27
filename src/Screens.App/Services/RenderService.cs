using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using QRCoder;
using Screens.App.Models;

namespace Screens.App.Services;

/// <summary>
/// Single rendering path used for both the live preview and the exported
/// file, so the two can never disagree (spec section 7). Callers scale the
/// output bitmap for on-screen display; export always renders at full
/// <see cref="CanvasSize"/> resolution.
/// </summary>
public sealed class RenderService
{
    private readonly FontRegistry _fonts;

    public RenderService(FontRegistry fonts) => _fonts = fonts;

    public SKBitmap Render(TemplateManifest manifest, IReadOnlyDictionary<string, string> values, bool watermark = false)
    {
        var imagePath = Path.Combine(manifest.SourceDirectory ?? "", manifest.Image);
        using var background = SKBitmap.Decode(imagePath)
            ?? throw new InvalidOperationException($"Could not decode template image '{imagePath}'.");

        var bitmap = new SKBitmap(manifest.Canvas.Width, manifest.Canvas.Height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            var destRect = new SKRect(0, 0, manifest.Canvas.Width, manifest.Canvas.Height);
            canvas.DrawBitmap(background, destRect);

            foreach (var field in manifest.Fields)
            {
                var text = values.TryGetValue(field.Id, out var v) ? v : field.Default;
                if (field.Type == "qr")
                {
                    DrawQr(canvas, field, text);
                    continue;
                }
                if (field.Type == "image")
                {
                    DrawImage(canvas, field, text);
                    continue;
                }
                if (field.Uppercase)
                    text = text.ToUpperInvariant();
                DrawField(canvas, field, text);
            }

            if (watermark)
                DrawWatermark(canvas, manifest.Canvas.Width, manifest.Canvas.Height);
        }

        return bitmap;
    }

    /// <summary>Encodes at full canvas resolution — never a screenshot of the on-screen preview.</summary>
    public void Export(SKBitmap bitmap, string path, ExportFormat format)
    {
        if (format == ExportFormat.Pdf)
        {
            ExportPdf(bitmap, path);
            return;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = format switch
        {
            ExportFormat.Jpg => image.Encode(SKEncodedImageFormat.Jpeg, 100),
            _ => image.Encode(SKEncodedImageFormat.Png, 100),
        };
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    private static void ExportPdf(SKBitmap bitmap, string path)
    {
        using var stream = new SKFileWStream(path);
        using var document = SKDocument.CreatePdf(stream);
        // One page sized to the image, at 72 DPI-equivalent points per pixel
        // scaled down so print output isn't absurdly large — full pixel
        // fidelity is preserved regardless since we draw the source bitmap
        // at native resolution into that page rect.
        var pageWidth = bitmap.Width * 72f / 150f;
        var pageHeight = bitmap.Height * 72f / 150f;
        using (var pageCanvas = document.BeginPage(pageWidth, pageHeight))
        {
            pageCanvas.DrawBitmap(bitmap, new SKRect(0, 0, pageWidth, pageHeight));
        }
        document.EndPage();
        document.Close();
    }

    private static void DrawWatermark(SKCanvas canvas, int width, int height)
    {
        const string text = "Made with Screens";
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 160),
            TextSize = Math.Max(16, width * 0.016f),
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
        };
        var textWidth = paint.MeasureText(text);
        var x = width - textWidth - 24;
        var y = height - 24;

        using var shadowPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 90), TextSize = paint.TextSize, Typeface = paint.Typeface };
        canvas.DrawText(text, x + 1, y + 1, shadowPaint);
        canvas.DrawText(text, x, y, paint);
    }

    private void DrawQr(SKCanvas canvas, TemplateField field, string content)
    {
        var box = new SKRect(
            (float)field.Box.X, (float)field.Box.Y,
            (float)(field.Box.X + field.Box.Width), (float)(field.Box.Y + field.Box.Height));

        if (string.IsNullOrWhiteSpace(content))
            return;

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var pngQr = new PngByteQRCode(qrData);
        var size = (int)Math.Max(1, Math.Min(box.Width, box.Height));
        var pngBytes = pngQr.GetGraphic(Math.Max(1, size / 33)); // ~33 modules across a typical QR; pixelsPerModule scales output near the box size
        using var qrBitmap = SKBitmap.Decode(pngBytes);
        if (qrBitmap is null)
            return;

        var dest = new SKRect(box.MidX - size / 2f, box.MidY - size / 2f, box.MidX + size / 2f, box.MidY + size / 2f);
        canvas.DrawBitmap(qrBitmap, dest);
    }

    /// <summary>Draws a user-chosen photo into a field's box with "cover" fit (fills the box,
    /// centered, cropping overflow rather than letterboxing) — the standard behavior for a
    /// device-mockup "screen" placeholder. <paramref name="path"/> is a local file path chosen
    /// via the field editor's photo picker; an empty/missing/undecodable path draws nothing,
    /// leaving the template's own background art (e.g. an empty phone screen) visible.</summary>
    private static void DrawImage(SKCanvas canvas, TemplateField field, string path)
    {
        var box = new SKRect(
            (float)field.Box.X, (float)field.Box.Y,
            (float)(field.Box.X + field.Box.Width), (float)(field.Box.Y + field.Box.Height));

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        using var photo = SKBitmap.Decode(path);
        if (photo is null)
            return;

        var boxAspect = box.Width / box.Height;
        var photoAspect = (float)photo.Width / photo.Height;

        SKRect srcRect;
        if (photoAspect > boxAspect)
        {
            // Photo is relatively wider than the box: crop its left/right edges.
            var cropWidth = photo.Height * boxAspect;
            var x = (photo.Width - cropWidth) / 2f;
            srcRect = new SKRect(x, 0, x + cropWidth, photo.Height);
        }
        else
        {
            // Photo is relatively taller than the box: crop its top/bottom edges.
            var cropHeight = photo.Width / boxAspect;
            var y = (photo.Height - cropHeight) / 2f;
            srcRect = new SKRect(0, y, photo.Width, y + cropHeight);
        }

        canvas.Save();
        canvas.ClipRect(box);
        canvas.DrawBitmap(photo, srcRect, box);
        canvas.Restore();
    }

    private void DrawField(SKCanvas canvas, TemplateField field, string text)
    {
        var box = new SKRect(
            (float)field.Box.X, (float)field.Box.Y,
            (float)(field.Box.X + field.Box.Width), (float)(field.Box.Y + field.Box.Height));

        var colorHex = field.RuntimeColorOverride ?? field.Color;
        var color = SKColor.Parse(colorHex);
        var alpha = (byte)(Math.Clamp(field.Opacity, 0.0, 1.0) * 255);
        color = color.WithAlpha(alpha);

        // None of the bundled Latin fonts carry Bengali glyphs, so Bengali text always renders
        // through the bundled Bengali face instead — regardless of what family the field/template
        // specifies — shaped via HarfBuzz so conjuncts and matras (vowel signs that reorder around
        // the consonant, e.g. রি) draw correctly instead of as separate, wrongly-positioned glyphs.
        // Latin/other text takes the exact same path it always has (plain SKPaint.DrawText).
        var isBengali = FontRegistry.ContainsBengali(text);
        var typeface = isBengali
            ? _fonts.ResolveBengali()
            : _fonts.Resolve(field.Font.Family, field.Font.Weight, field.Font.Italic);
        using var shaper = isBengali ? new SKShaper(typeface) : null;

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Typeface = typeface,
            TextAlign = SKTextAlign.Left,
            // There's only one bundled Bengali weight (see FontRegistry.ResolveBengali), so
            // "Bold" is approximated with synthetic emboldening rather than a second font file.
            FakeBoldText = isBengali && field.Font.Weight.Equals("Bold", StringComparison.OrdinalIgnoreCase),
        };

        float MeasureWidth(string s) => shaper is not null ? shaper.Shape(s, paint).Width : paint.MeasureText(s);

        var fontSize = (float)(field.RuntimeSizeOverride ?? field.Font.Size);
        var minSize = fontSize * 0.5f;

        List<string> lines;
        string displayText = text;

        while (true)
        {
            paint.TextSize = fontSize;
            lines = field.Multiline
                ? WrapLines(displayText, paint, box.Width)
                : new List<string> { displayText };

            var lineHeightPx = fontSize * (float)field.LineHeight;
            var totalHeight = lines.Count * lineHeightPx;
            var widestLine = 0f;
            foreach (var line in lines)
                widestLine = Math.Max(widestLine, MeasureWidth(line));

            var overflowsHeight = field.Multiline ? totalHeight > box.Height : lineHeightPx > box.Height;
            var overflowsWidth = !field.Multiline && widestLine > box.Width;

            if (!overflowsHeight && !overflowsWidth)
                break;

            if (!field.AutoShrink)
            {
                if (!field.Multiline)
                    displayText = Ellipsise(displayText, paint, box.Width);
                break;
            }

            fontSize -= 1f;
            if (fontSize <= minSize)
            {
                fontSize = minSize;
                paint.TextSize = fontSize;
                if (!field.Multiline)
                {
                    displayText = Ellipsise(displayText, paint, box.Width);
                }
                else
                {
                    lines = WrapLines(displayText, paint, box.Width);
                    lines = TrimToHeight(lines, box.Height, fontSize * (float)field.LineHeight, paint);
                }
                break;
            }
        }

        paint.TextSize = fontSize;
        var finalLineHeight = fontSize * (float)field.LineHeight;
        var finalTotalHeight = lines.Count * finalLineHeight;

        float startY = field.VerticalAlign switch
        {
            "middle" => box.Top + (box.Height - finalTotalHeight) / 2f,
            "bottom" => box.Bottom - finalTotalHeight,
            _ => box.Top,
        };

        canvas.Save();
        canvas.ClipRect(box);

        SKPaint? shadowPaint = null;
        if (field.Shadow)
        {
            shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0, 0, 0, (byte)(alpha * 0.55)),
                Typeface = typeface,
                TextSize = fontSize,
                TextAlign = SKTextAlign.Left,
            };
        }

        var metrics = paint.FontMetrics;
        float y = startY - metrics.Ascent + (finalLineHeight - fontSize) / 2f;
        foreach (var line in lines)
        {
            var lineWidth = MeasureWidth(line);
            float x = field.Align switch
            {
                "center" => box.MidX - lineWidth / 2f,
                "right" => box.Right - lineWidth,
                _ => box.Left,
            };
            if (shaper is not null)
            {
                if (shadowPaint is not null)
                    canvas.DrawShapedText(shaper, line, x + fontSize * 0.03f, y + fontSize * 0.05f, shadowPaint);
                canvas.DrawShapedText(shaper, line, x, y, paint);
            }
            else
            {
                if (shadowPaint is not null)
                    canvas.DrawText(line, x + fontSize * 0.03f, y + fontSize * 0.05f, shadowPaint);
                canvas.DrawText(line, x, y, paint);
            }
            y += finalLineHeight;
        }

        shadowPaint?.Dispose();
        canvas.Restore();
    }

    private static List<string> WrapLines(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = "";
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (paint.MeasureText(candidate) > maxWidth && current.Length > 0)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }
            lines.Add(current);
        }
        return lines;
    }

    private static List<string> TrimToHeight(List<string> lines, float maxHeight, float lineHeightPx, SKPaint paint)
    {
        var maxLines = Math.Max(1, (int)(maxHeight / lineHeightPx));
        if (lines.Count <= maxLines)
            return lines;
        var trimmed = lines.GetRange(0, maxLines);
        trimmed[^1] = Ellipsise(trimmed[^1] + "…", paint, float.MaxValue);
        return trimmed;
    }

    private static string Ellipsise(string text, SKPaint paint, float maxWidth)
    {
        if (paint.MeasureText(text) <= maxWidth)
            return text;
        const string ellipsis = "…";
        var truncated = text;
        while (truncated.Length > 0 && paint.MeasureText(truncated + ellipsis) > maxWidth)
            truncated = truncated[..^1];
        return truncated + ellipsis;
    }
}
