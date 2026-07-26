using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;
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

    public SKBitmap Render(TemplateManifest manifest, IReadOnlyDictionary<string, string> values)
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
                if (field.Uppercase)
                    text = text.ToUpperInvariant();
                DrawField(canvas, field, text);
            }
        }

        return bitmap;
    }

    /// <summary>Encodes at full canvas resolution — never a screenshot of the on-screen preview.</summary>
    public void Export(SKBitmap bitmap, string path, ExportFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = format switch
        {
            ExportFormat.Jpg => image.Encode(SKEncodedImageFormat.Jpeg, 92),
            _ => image.Encode(SKEncodedImageFormat.Png, 100),
        };
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    private void DrawField(SKCanvas canvas, TemplateField field, string text)
    {
        var box = new SKRect(
            (float)field.Box.X, (float)field.Box.Y,
            (float)(field.Box.X + field.Box.Width), (float)(field.Box.Y + field.Box.Height));

        var color = SKColor.Parse(field.Color);
        var typeface = _fonts.Resolve(field.Font.Family, field.Font.Weight, field.Font.Italic);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Typeface = typeface,
            TextAlign = SKTextAlign.Left,
        };

        var fontSize = (float)field.Font.Size;
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
                widestLine = Math.Max(widestLine, paint.MeasureText(line));

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

        var metrics = paint.FontMetrics;
        float y = startY - metrics.Ascent + (finalLineHeight - fontSize) / 2f;
        foreach (var line in lines)
        {
            var lineWidth = paint.MeasureText(line);
            float x = field.Align switch
            {
                "center" => box.MidX - lineWidth / 2f,
                "right" => box.Right - lineWidth,
                _ => box.Left,
            };
            canvas.DrawText(line, x, y, paint);
            y += finalLineHeight;
        }

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
