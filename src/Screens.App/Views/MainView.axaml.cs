using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Screens.App.Models;
using Screens.App.Services;
using Screens.App.ViewModels;
using SkiaSharp;

namespace Screens.App.Views;

public partial class MainView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    private FieldEditorItemViewModel? _draggingField;
    private Point _dragLastPointerPos;
    private double _dragScale = 1.0;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
            {
                vm.ExportRequested += OnExportRequested;
                vm.ClipboardExportRequested += OnClipboardExportRequested;
            }
        };
    }

    private void OnTemplateTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: TemplateManifest manifest } && Vm is { } vm)
            vm.SelectedTemplate = manifest;
    }

    private static FilePickerFileType[] FileTypeChoices(ExportFormat requested) => requested switch
    {
        ExportFormat.Jpg => new[] { new FilePickerFileType("JPEG image") { Patterns = new[] { "*.jpg" } } },
        ExportFormat.Pdf => new[] { new FilePickerFileType("PDF document") { Patterns = new[] { "*.pdf" } } },
        _ => new[] { new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } } },
    };

    private async void OnExportRequested(object? sender, (SKBitmapHolder Bitmap, string DefaultFileName, ExportFormat Format) e)
    {
        using var holder = e.Bitmap;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var settings = new SettingsService();
        Directory.CreateDirectory(settings.DefaultExportDirectory);
        var startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(settings.DefaultExportDirectory);

        var ext = e.Format switch { ExportFormat.Jpg => "jpg", ExportFormat.Pdf => "pdf", _ => "png" };
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export",
            SuggestedFileName = e.DefaultFileName,
            SuggestedStartLocation = startFolder,
            FileTypeChoices = FileTypeChoices(e.Format),
            DefaultExtension = ext,
        });

        if (file is null)
            return;

        var path = file.Path.LocalPath;
        Vm?.WriteExportFile(holder.Bitmap, path, e.Format);
        Vm?.CompleteExport(path);
    }

    private void OnClipboardExportRequested(object? sender, SKBitmapHolder holder)
    {
        using var h = holder;
        if (OperatingSystem.IsWindows())
        {
            Win32Clipboard.SetBitmap(h.Bitmap);
            Vm?.CompleteExport("(clipboard)");
        }
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm?.LastExportPath is not { } path || path == "(clipboard)")
            return;
        var folder = Path.GetDirectoryName(path);
        if (folder is null)
            return;
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private void OnThemeLightClicked(object? sender, RoutedEventArgs e) { if (Vm is { } vm) vm.Theme = ThemePreference.Light; }
    private void OnThemeDarkClicked(object? sender, RoutedEventArgs e) { if (Vm is { } vm) vm.Theme = ThemePreference.Dark; }
    private void OnThemeSystemClicked(object? sender, RoutedEventArgs e) { if (Vm is { } vm) vm.Theme = ThemePreference.System; }

    private void OnLanguageEnglishClicked(object? sender, RoutedEventArgs e) { if (Vm is { } vm) vm.Language = AppLanguage.English; }
    private void OnLanguageChineseClicked(object? sender, RoutedEventArgs e) { if (Vm is { } vm) vm.Language = AppLanguage.Chinese; }

    private void OnRenewClicked(object? sender, RoutedEventArgs e)
    {
        var config = AppConfig.Load();
        var url = string.IsNullOrWhiteSpace(config.RenewUrl) ? "https://github.com/RafiqulIslamNiL100/screens-fnl-app" : config.RenewUrl;
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private void OnSignOutClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window { DataContext: ShellViewModel shell })
            shell.SignOut();
    }

    private void OnCommandPaletteBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender == e.Source && Vm is { } vm)
            vm.CloseCommandPaletteCommand.Execute(null);
    }

    // ---- Drag-to-reposition -------------------------------------------

    private void OnFieldDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: FieldEditorItemViewModel field } control)
            return;

        _draggingField = field;
        _dragLastPointerPos = e.GetPosition(control.Parent as Visual ?? control);
        // The overlay Canvas sits inside a Viewbox that scales canvas-pixel
        // units to on-screen pixels; dividing pointer-space deltas by that
        // scale converts them back to canvas-pixel deltas MoveField expects.
        _dragScale = control.Parent is Visual parent && parent.Bounds.Width > 0 && Vm?.SelectedTemplate is { } t
            ? parent.Bounds.Width / t.Canvas.Width
            : 1.0;
        e.Pointer.Capture(control);
    }

    private void OnFieldDragMove(object? sender, PointerEventArgs e)
    {
        if (_draggingField is null || sender is not Control { Tag: FieldEditorItemViewModel field } control || field != _draggingField)
            return;
        if (!Equals(e.Pointer.Captured, control))
            return;

        var parent = control.Parent as Visual ?? control;
        var pos = e.GetPosition(parent);
        var dx = (pos.X - _dragLastPointerPos.X) / _dragScale;
        var dy = (pos.Y - _dragLastPointerPos.Y) / _dragScale;
        _dragLastPointerPos = pos;

        Vm?.MoveField(field, dx, dy);
        Canvas.SetLeft(control, field.Field.Box.X);
        Canvas.SetTop(control, field.Field.Box.Y);
    }

    private void OnFieldDragEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control control)
            e.Pointer.Capture(null);
        _draggingField = null;
    }
}

/// <summary>Win32 clipboard write (CF_DIB) for "copy export to clipboard" — Avalonia's
/// cross-platform IClipboard only supports text, so this is a small direct P/Invoke
/// path used only on the platform we actually ship (win-x64).</summary>
[SupportedOSPlatform("windows")]
internal static class Win32Clipboard
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndOwner);
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_DIB = 8;
    private const uint GMEM_MOVEABLE = 0x0002;

    public static void SetBitmap(SKBitmap bitmap)
    {
        // Encode as a 32bpp BGRA DIB (BITMAPINFOHEADER + top-down pixel data),
        // which is what CF_DIB expects.
        var width = bitmap.Width;
        var height = bitmap.Height;
        const int headerSize = 40;
        var pixelBytes = width * height * 4;
        var dib = new byte[headerSize + pixelBytes];

        void WriteInt32(int offset, int value) => BitConverter.GetBytes(value).CopyTo(dib, offset);
        void WriteInt16(int offset, short value) => BitConverter.GetBytes(value).CopyTo(dib, offset);

        WriteInt32(0, headerSize);
        WriteInt32(4, width);
        WriteInt32(8, -height); // negative = top-down
        WriteInt16(12, 1);      // planes
        WriteInt16(14, 32);     // bits per pixel
        WriteInt32(16, 0);      // BI_RGB, no compression
        WriteInt32(20, pixelBytes);

        using var pixmap = bitmap.PeekPixels();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                var offset = headerSize + (y * width + x) * 4;
                dib[offset + 0] = c.Blue;
                dib[offset + 1] = c.Green;
                dib[offset + 2] = c.Red;
                dib[offset + 3] = c.Alpha;
            }
        }

        var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)dib.Length);
        if (hGlobal == IntPtr.Zero)
            return;

        var target = GlobalLock(hGlobal);
        if (target == IntPtr.Zero)
            return;
        Marshal.Copy(dib, 0, target, dib.Length);
        GlobalUnlock(hGlobal);

        if (!OpenClipboard(IntPtr.Zero))
            return;
        try
        {
            EmptyClipboard();
            SetClipboardData(CF_DIB, hGlobal);
        }
        finally
        {
            CloseClipboard();
        }
    }
}
