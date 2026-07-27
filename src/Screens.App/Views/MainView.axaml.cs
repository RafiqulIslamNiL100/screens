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
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.SelectedTemplate))
                        RecomputeFitScale();
                };
                // The view model already has a SelectedTemplate by the time DataContext is
                // assigned (LoadTemplates runs in its constructor, at app startup, long before
                // this view is ever shown) — subscribing above only catches *future* selections.
                // Without this, "Fit" silently stays stuck at its 1.0 default until something
                // else happens to trigger a resize, rendering the template at literal 100% (often
                // much bigger than the viewport) despite the toolbar still reading "Fit".
                RecomputeFitScale();
            }
        };

        var viewport = this.FindControl<Border>("CanvasViewport");
        if (viewport is not null)
        {
            viewport.SizeChanged += (_, _) => RecomputeFitScale();
            // Also recompute once the control has its final layout bounds — SizeChanged can be a
            // no-op if this view was measured while hidden (Screen starts at Splash/Auth) and its
            // Bounds happen not to change again once it becomes visible.
            viewport.AttachedToVisualTree += (_, _) => RecomputeFitScale();
        }
    }

    /// <summary>"Fit" tracks whatever the current canvas viewport size is, recomputed whenever
    /// the window resizes or a different (differently-sized) template is selected.</summary>
    private void RecomputeFitScale()
    {
        var viewport = this.FindControl<Border>("CanvasViewport");
        if (viewport is null || Vm?.SelectedTemplate is not { } template || template.Canvas.Width <= 0 || template.Canvas.Height <= 0)
            return;

        const double margin = 48; // matches the LayoutTransformControl's Margin="24" on each side
        var availWidth = Math.Max(1, viewport.Bounds.Width - margin);
        var availHeight = Math.Max(1, viewport.Bounds.Height - margin);
        var scale = Math.Min(availWidth / template.Canvas.Width, availHeight / template.Canvas.Height);
        Vm.SetFitScale(scale);

        // Force a fresh measure/arrange pass so the ScrollViewer's scrollable extent always
        // matches the just-applied scale, instead of occasionally keeping a stale (larger) extent
        // from before this recompute — which left dead scrollable space and could leave the view
        // scrolled to show only a small corner of the template rather than the centered whole.
        var transform = this.FindControl<Avalonia.Controls.LayoutTransformControl>("CanvasTransform");
        transform?.InvalidateMeasure();
        viewport.InvalidateMeasure();
    }

    private void OnCanvasWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && Vm is { } vm)
        {
            vm.ZoomByWheel(e.Delta.Y);
            e.Handled = true;
        }
    }

    private void OnTemplateTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: TemplateManifest manifest } && Vm is { } vm)
            vm.SelectedTemplate = manifest;
    }

    private async void OnChoosePhotoClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: FieldEditorItemViewModel field })
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a photo",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" } },
            },
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file is null)
            return;

        field.Value = file.Path.LocalPath;
    }

    private void OnClearPhotoClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: FieldEditorItemViewModel field })
            field.Value = "";
    }

    private async void OnScanImageClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Scan a picture",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" } },
            },
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file is null)
            return;

        await vm.ScanImageAsync(file.Path.LocalPath);
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
        // The overlay Canvas is inside a LayoutTransformControl whose Bounds stay in
        // pre-transform (canvas-pixel) space, so it can't be used to recover the
        // on-screen scale. Use EffectiveZoom directly instead.
        _dragScale = Vm?.EffectiveZoom > 0 ? Vm.EffectiveZoom : 1.0;
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
