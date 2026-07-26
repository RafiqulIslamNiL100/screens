using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Screens.App.Models;
using Screens.App.Services;

namespace Screens.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly TemplateService _templates;
    private readonly RenderService _render;
    private readonly SettingsService _settings;
    private readonly UpdateService _update;
    private readonly FontRegistry _fonts;

    private CancellationTokenSource? _debounceCts;

    [ObservableProperty] private TemplateManifest? _selectedTemplate;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private bool _zoomToFit = true;
    [ObservableProperty] private string? _toastMessage;
    [ObservableProperty] private bool _showToast;
    [ObservableProperty] private string? _lastExportPath;
    [ObservableProperty] private string? _updateBannerText;
    [ObservableProperty] private bool _showUpdateBanner;
    [ObservableProperty] private UpdateInfo? _pendingUpdate;
    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private ThemePreference _theme;
    [ObservableProperty] private string _lastCheckedText = "Never checked";
    [ObservableProperty] private string _currentVersionText = $"Version {UpdateService.CurrentVersion}";
    [ObservableProperty] private string? _checkForUpdatesResult;

    public ObservableCollection<TemplateManifest> Templates { get; } = new();
    public ObservableCollection<FieldEditorItemViewModel> Fields { get; } = new();
    public ObservableCollection<TemplateLoadWarning> Warnings { get; } = new();

    public event EventHandler<(SKBitmapHolder Bitmap, string DefaultFileName)>? ExportRequested;

    public MainViewModel(TemplateService templates, RenderService render, SettingsService settings, UpdateService update, FontRegistry fonts)
    {
        _templates = templates;
        _render = render;
        _settings = settings;
        _update = update;
        _fonts = fonts;

        var appSettings = _settings.Load();
        Theme = appSettings.Theme;

        LoadTemplates(appSettings.LastTemplateId);
    }

    private void LoadTemplates(string? lastTemplateId)
    {
        Templates.Clear();
        Warnings.Clear();
        foreach (var t in _templates.LoadAll())
            Templates.Add(t);
        foreach (var w in _templates.Warnings)
            Warnings.Add(w);

        SelectedTemplate = Templates.FirstOrDefault(t => t.Id == lastTemplateId) ?? Templates.FirstOrDefault();
    }

    partial void OnSelectedTemplateChanged(TemplateManifest? value)
    {
        Fields.Clear();
        if (value is null)
        {
            PreviewImage = null;
            return;
        }

        foreach (var field in value.Fields)
        {
            var item = new FieldEditorItemViewModel(field);
            item.ValueChanged += (_, _) => ScheduleRender();
            Fields.Add(item);
        }

        var appSettings = _settings.Load();
        appSettings.LastTemplateId = value.Id;
        _settings.Save(appSettings);

        RenderNow();
    }

    /// <summary>Debounced ~80ms so typing stays smooth (spec section 7).</summary>
    private void ScheduleRender()
    {
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebounceAsync(cts.Token);
    }

    private async Task DebounceAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(80, token);
            if (!token.IsCancellationRequested)
                RenderNow();
        }
        catch (TaskCanceledException) { }
    }

    private void RenderNow()
    {
        if (SelectedTemplate is null)
            return;

        var values = Fields.ToDictionary(f => f.Field.Id, f => f.Value);
        using var bitmap = _render.Render(SelectedTemplate, values);
        PreviewImage = SkiaInterop.ToAvaloniaBitmap(bitmap);
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        foreach (var f in Fields)
            f.Reset();
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var f in Fields)
            f.Clear();
    }

    [RelayCommand]
    private void ToggleZoom() => ZoomToFit = !ZoomToFit;

    [RelayCommand]
    private void Export()
    {
        if (SelectedTemplate is null)
            return;

        var values = Fields.ToDictionary(f => f.Field.Id, f => f.Value);
        var bitmap = _render.Render(SelectedTemplate, values);
        var fileName = $"{SelectedTemplate.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        ExportRequested?.Invoke(this, (new SKBitmapHolder(bitmap), fileName));
    }

    public void CompleteExport(string path)
    {
        LastExportPath = path;
        ToastMessage = $"Exported {Path.GetFileName(path)}";
        ShowToast = true;
    }

    [RelayCommand]
    private void DismissToast() => ShowToast = false;

    [RelayCommand]
    private void DismissUpdateBanner() => ShowUpdateBanner = false;

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (PendingUpdate is null)
            return;
        await _update.DownloadAndInstallAsync(PendingUpdate);
        // Caller (App shutdown) exits the process so the installer can overwrite files.
    }

    public async Task CheckForUpdatesOnStartupAsync()
    {
        var result = await _update.CheckAsync();
        LastCheckedText = _update.LastCheckedAt?.ToLocalTime().ToString("g") ?? "Never checked";
        if (result.UpdateAvailable && result.Info is not null)
        {
            PendingUpdate = result.Info;
            UpdateBannerText = $"Screens {result.Info.Version} is available. {result.Info.Notes}";
            ShowUpdateBanner = true;
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesManualAsync()
    {
        var result = await _update.CheckAsync();
        LastCheckedText = _update.LastCheckedAt?.ToLocalTime().ToString("g") ?? "Never checked";
        if (result.UpdateAvailable && result.Info is not null)
        {
            PendingUpdate = result.Info;
            UpdateBannerText = $"Screens {result.Info.Version} is available. {result.Info.Notes}";
            ShowUpdateBanner = true;
            CheckForUpdatesResult = $"Update available: {result.Info.Version}";
        }
        else
        {
            CheckForUpdatesResult = "You're up to date.";
        }
    }

    partial void OnThemeChanged(ThemePreference value)
    {
        var appSettings = _settings.Load();
        appSettings.Theme = value;
        _settings.Save(appSettings);
        ThemeChanged?.Invoke(this, value);
    }

    public event EventHandler<ThemePreference>? ThemeChanged;

    [RelayCommand]
    private void ToggleSettings() => ShowSettings = !ShowSettings;
}

/// <summary>Wraps an SKBitmap so it can travel through an EventArgs without a hard SkiaSharp
/// dependency on every subscriber; caller (View) is responsible for disposing it.</summary>
public sealed class SKBitmapHolder : IDisposable
{
    public SkiaSharp.SKBitmap Bitmap { get; }
    public SKBitmapHolder(SkiaSharp.SKBitmap bitmap) => Bitmap = bitmap;
    public void Dispose() => Bitmap.Dispose();
}
