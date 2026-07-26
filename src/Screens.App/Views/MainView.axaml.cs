using System;
using System.Diagnostics;
using System.IO;
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

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
                vm.ExportRequested += OnExportRequested;
        };
    }

    private void OnTemplateTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: TemplateManifest manifest } && Vm is { } vm)
            vm.SelectedTemplate = manifest;
    }

    private async void OnExportRequested(object? sender, (SKBitmapHolder Bitmap, string DefaultFileName) e)
    {
        using var holder = e.Bitmap;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var settings = new SettingsService();
        Directory.CreateDirectory(settings.DefaultExportDirectory);
        var startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(settings.DefaultExportDirectory);

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export image",
            SuggestedFileName = e.DefaultFileName,
            SuggestedStartLocation = startFolder,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("JPEG image") { Patterns = new[] { "*.jpg" } },
            },
            DefaultExtension = "png",
        });

        if (file is null)
            return;

        var path = file.Path.LocalPath;
        var format = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? ExportFormat.Jpg
            : ExportFormat.Png;

        using var image = SKImage.FromBitmap(holder.Bitmap);
        using var data = format == ExportFormat.Jpg
            ? image.Encode(SKEncodedImageFormat.Jpeg, 92)
            : image.Encode(SKEncodedImageFormat.Png, 100);
        await using var stream = File.Create(path);
        data.SaveTo(stream);

        Vm?.CompleteExport(path);
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm?.LastExportPath is not { } path)
            return;
        var folder = Path.GetDirectoryName(path);
        if (folder is null)
            return;
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private void OnThemeLightClicked(object? sender, RoutedEventArgs e) { if (Vm is { } vm) vm.Theme = ThemePreference.Light; }
    private void OnThemeDarkClicked(object? sender, RoutedEventArgs e) { if (Vm is { } vm) vm.Theme = ThemePreference.Dark; }
    private void OnThemeSystemClicked(object? sender, RoutedEventArgs e) { if (Vm is { } vm) vm.Theme = ThemePreference.System; }

    private void OnSignOutClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window { DataContext: ShellViewModel shell })
            shell.SignOut();
    }
}
