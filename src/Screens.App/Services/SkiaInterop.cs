using System.IO;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Screens.App.Services;

/// <summary>Bridges SkiaSharp render output to an Avalonia bitmap for on-screen display.</summary>
public static class SkiaInterop
{
    public static Bitmap ToAvaloniaBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}
