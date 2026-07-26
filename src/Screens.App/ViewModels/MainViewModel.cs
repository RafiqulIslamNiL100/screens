using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    private readonly LicenseService _license;
    public LocalizationService Loc { get; }

    private CancellationTokenSource? _debounceCts;
    private List<TemplateManifest> _allTemplates = new();
    private bool _isApplyingDraft;

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
    [ObservableProperty] private AppLanguage _language;
    [ObservableProperty] private string _lastCheckedText = "Never checked";
    [ObservableProperty] private string _currentVersionText = $"Version {UpdateService.CurrentVersion}";
    [ObservableProperty] private string? _checkForUpdatesResult;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _watermarkEnabled;
    [ObservableProperty] private bool _snapToGridEnabled = true;
    [ObservableProperty] private string? _licenseCountdownText;
    [ObservableProperty] private bool _showRenewLink;
    [ObservableProperty] private bool _showCommandPalette;
    [ObservableProperty] private string _commandPaletteQuery = "";
    [ObservableProperty] private string _newPresetName = "";
    [ObservableProperty] private string _newPresetFormat = "png";

    public ObservableCollection<TemplateManifest> Templates { get; } = new();
    public ObservableCollection<TemplateManifest> RecentTemplates { get; } = new();
    public ObservableCollection<FieldEditorItemViewModel> Fields { get; } = new();
    public ObservableCollection<TemplateLoadWarning> Warnings { get; } = new();
    public ObservableCollection<ExportPreset> ExportPresets { get; } = new();
    public ObservableCollection<CommandPaletteItem> CommandPaletteItems { get; } = new();
    public ObservableCollection<CommandPaletteItem> FilteredCommandPaletteItems { get; } = new();

    public event EventHandler<(SKBitmapHolder Bitmap, string DefaultFileName, ExportFormat Format)>? ExportRequested;
    public event EventHandler<SKBitmapHolder>? ClipboardExportRequested;
    public event EventHandler<ThemePreference>? ThemeChanged;

    public MainViewModel(TemplateService templates, RenderService render, SettingsService settings, UpdateService update, FontRegistry fonts, LicenseService license, LocalizationService loc)
    {
        _templates = templates;
        _render = render;
        _settings = settings;
        _update = update;
        _fonts = fonts;
        _license = license;
        Loc = loc;

        var appSettings = _settings.Load();
        Theme = appSettings.Theme;
        Language = appSettings.Language;
        WatermarkEnabled = appSettings.WatermarkEnabled;
        SnapToGridEnabled = appSettings.SnapToGridEnabled;
        foreach (var p in appSettings.ExportPresets)
            ExportPresets.Add(p);

        Loc.Language = Language;
        _license.StateChanged += _ => UpdateLicenseCountdown();
        UpdateLicenseCountdown();

        BuildCommandPaletteItems();

        LoadTemplates(appSettings.LastTemplateId);
    }

    private void LoadTemplates(string? lastTemplateId)
    {
        _allTemplates = _templates.LoadAll().ToList();
        Warnings.Clear();
        foreach (var w in _templates.Warnings)
            Warnings.Add(w);

        ApplyTemplateFilter();
        RefreshRecentTemplates();

        SelectedTemplate = Templates.FirstOrDefault(t => t.Id == lastTemplateId) ?? Templates.FirstOrDefault();
    }

    private void ApplyTemplateFilter()
    {
        var query = SearchText.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _allTemplates
            : _allTemplates.Where(t =>
                t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Category.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        var previouslySelected = SelectedTemplate?.Id;
        Templates.Clear();
        foreach (var t in matches.OrderBy(t => t.Category).ThenBy(t => t.Name))
            Templates.Add(t);

        if (previouslySelected is not null && Templates.All(t => t.Id != previouslySelected))
        {
            // The current selection got filtered out; leave it displayed rather
            // than yanking the canvas away mid-search — it reappears if the
            // user clears the search.
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyTemplateFilter();

    private void RefreshRecentTemplates()
    {
        var appSettings = _settings.Load();
        RecentTemplates.Clear();
        foreach (var id in appSettings.RecentTemplateIds)
        {
            var match = _allTemplates.FirstOrDefault(t => t.Id == id);
            if (match is not null)
                RecentTemplates.Add(match);
        }
    }

    private void RecordRecentTemplate(string templateId)
    {
        var appSettings = _settings.Load();
        appSettings.RecentTemplateIds.Remove(templateId);
        appSettings.RecentTemplateIds.Insert(0, templateId);
        if (appSettings.RecentTemplateIds.Count > 8)
            appSettings.RecentTemplateIds.RemoveRange(8, appSettings.RecentTemplateIds.Count - 8);
        _settings.Save(appSettings);
        RefreshRecentTemplates();
    }

    partial void OnSelectedTemplateChanged(TemplateManifest? value)
    {
        Fields.Clear();
        if (value is null)
        {
            PreviewImage = null;
            return;
        }

        var appSettings = _settings.Load();
        appSettings.Drafts.TryGetValue(value.Id, out var draft);

        _isApplyingDraft = true;
        foreach (var field in value.Fields)
        {
            var item = new FieldEditorItemViewModel(field);
            if (draft is not null && draft.TryGetValue(field.Id, out var savedValue))
                item.Value = savedValue;
            item.ValueChanged += (_, _) => { ScheduleRender(); SaveDraft(); };
            Fields.Add(item);
        }
        _isApplyingDraft = false;

        appSettings.LastTemplateId = value.Id;
        _settings.Save(appSettings);
        RecordRecentTemplate(value.Id);

        RenderNow();
    }

    private void SaveDraft()
    {
        if (_isApplyingDraft || SelectedTemplate is null)
            return;
        var appSettings = _settings.Load();
        appSettings.Drafts[SelectedTemplate.Id] = Fields.ToDictionary(f => f.Field.Id, f => f.Value);
        _settings.Save(appSettings);
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
        using var bitmap = _render.Render(SelectedTemplate, values, WatermarkEnabled);
        PreviewImage = SkiaInterop.ToAvaloniaBitmap(bitmap);
    }

    /// <summary>Repositions a field's box in canvas-pixel space (drag-to-reposition), optionally snapped to a grid.</summary>
    public void MoveField(FieldEditorItemViewModel field, double deltaCanvasX, double deltaCanvasY)
    {
        if (SelectedTemplate is null)
            return;

        var box = field.Field.Box;
        var newX = box.X + deltaCanvasX;
        var newY = box.Y + deltaCanvasY;

        if (SnapToGridEnabled)
        {
            const double grid = 10;
            newX = Math.Round(newX / grid) * grid;
            newY = Math.Round(newY / grid) * grid;
        }

        newX = Math.Clamp(newX, 0, Math.Max(0, SelectedTemplate.Canvas.Width - box.Width));
        newY = Math.Clamp(newY, 0, Math.Max(0, SelectedTemplate.Canvas.Height - box.Height));

        box.X = newX;
        box.Y = newY;
        ScheduleRender();
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        // Reloading from disk also undoes any drag-repositioned boxes, not
        // just field text — "Reset" means back to the template as shipped.
        var currentId = SelectedTemplate?.Id;
        if (currentId is not null)
            LoadTemplates(currentId);
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
    private void Export() => ExportInternal(ExportFormat.Png);

    [RelayCommand]
    private void ExportAllFormats()
    {
        ExportInternal(ExportFormat.Png);
        ExportInternal(ExportFormat.Jpg);
        ExportInternal(ExportFormat.Pdf);
    }

    [RelayCommand]
    private void ExportToClipboard()
    {
        if (SelectedTemplate is null)
            return;
        var values = Fields.ToDictionary(f => f.Field.Id, f => f.Value);
        var bitmap = _render.Render(SelectedTemplate, values, WatermarkEnabled);
        ClipboardExportRequested?.Invoke(this, new SKBitmapHolder(bitmap));
    }

    [RelayCommand]
    private void ExportWithPreset(ExportPreset preset)
    {
        if (SelectedTemplate is null)
            return;
        var format = preset.Format switch { "jpg" => ExportFormat.Jpg, "pdf" => ExportFormat.Pdf, _ => ExportFormat.Png };
        ExportInternal(format, preset.Folder);
    }

    private void ExportInternal(ExportFormat format, string? folderOverride = null)
    {
        if (SelectedTemplate is null)
            return;

        var values = Fields.ToDictionary(f => f.Field.Id, f => f.Value);
        var bitmap = _render.Render(SelectedTemplate, values, WatermarkEnabled);
        var ext = format switch { ExportFormat.Jpg => "jpg", ExportFormat.Pdf => "pdf", _ => "png" };
        var fileName = $"{SelectedTemplate.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}";
        ExportRequested?.Invoke(this, (new SKBitmapHolder(bitmap), fileName, format));
    }

    [RelayCommand]
    private void AddExportPreset()
    {
        if (string.IsNullOrWhiteSpace(NewPresetName))
            return;
        var preset = new ExportPreset { Name = NewPresetName.Trim(), Format = NewPresetFormat };
        ExportPresets.Add(preset);
        var appSettings = _settings.Load();
        appSettings.ExportPresets.Add(preset);
        _settings.Save(appSettings);
        NewPresetName = "";
    }

    [RelayCommand]
    private void RemoveExportPreset(ExportPreset preset)
    {
        ExportPresets.Remove(preset);
        var appSettings = _settings.Load();
        appSettings.ExportPresets.RemoveAll(p => p.Name == preset.Name);
        _settings.Save(appSettings);
    }

    [RelayCommand]
    private void DuplicateTemplate()
    {
        if (SelectedTemplate is null)
            return;

        var source = SelectedTemplate;
        var newId = $"{source.Id}-copy-{DateTime.Now:HHmmss}";
        var destDir = _settings.TemplatesDirectory;
        Directory.CreateDirectory(destDir);

        var sourceImagePath = Path.Combine(source.SourceDirectory ?? "", source.Image);
        var newImageName = $"{newId}.png";
        File.Copy(sourceImagePath, Path.Combine(destDir, newImageName), overwrite: true);

        var clone = new TemplateManifest
        {
            Id = newId,
            Name = $"{source.Name} (Copy)",
            Category = source.Category,
            Image = newImageName,
            Canvas = new CanvasSize { Width = source.Canvas.Width, Height = source.Canvas.Height },
            Fields = source.Fields.Select(f => new TemplateField
            {
                Id = f.Id,
                Type = f.Type,
                Label = f.Label,
                Default = f.Default,
                Placeholder = f.Placeholder,
                MaxLength = f.MaxLength,
                Multiline = f.Multiline,
                Box = new FieldBox { X = f.Box.X, Y = f.Box.Y, Width = f.Box.Width, Height = f.Box.Height },
                Align = f.Align,
                VerticalAlign = f.VerticalAlign,
                Font = new FieldFont { Family = f.Font.Family, Size = f.Font.Size, Weight = f.Font.Weight, Italic = f.Font.Italic },
                Color = f.Color,
                LineHeight = f.LineHeight,
                LetterSpacing = f.LetterSpacing,
                Uppercase = f.Uppercase,
                AutoShrink = f.AutoShrink,
                UserEditableColor = true,
                UserEditableSize = true,
                Opacity = f.Opacity,
                Shadow = f.Shadow,
            }).ToList(),
        };

        var json = JsonSerializer.Serialize(clone, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(destDir, $"{newId}.json"), json);

        LoadTemplates(newId);
        ToastMessage = $"Duplicated as \"{clone.Name}\"";
        ShowToast = true;
    }

    /// <summary>Encodes and writes a rendered bitmap to disk — the View owns the save-file dialog, this owns the actual encode.</summary>
    public void WriteExportFile(SkiaSharp.SKBitmap bitmap, string path, ExportFormat format) => _render.Export(bitmap, path, format);

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

    partial void OnLanguageChanged(AppLanguage value)
    {
        var appSettings = _settings.Load();
        appSettings.Language = value;
        _settings.Save(appSettings);
        Loc.Language = value;
        BuildCommandPaletteItems();
        UpdateLicenseCountdown();
        OnPropertyChanged(string.Empty);
    }

    // ---- Localized labels (MainView.axaml binds to these) --------------
    public string FieldsLabel => Loc.T("Main.Fields");
    public string ResetLabel => Loc.T("Main.Reset");
    public string ClearAllLabel => Loc.T("Main.ClearAll");
    public string ExportLabel => Loc.T("Main.Export");
    public string ZoomToggleLabel => Loc.T("Main.ZoomToggle");
    public string SettingsLabel => Loc.T("Main.Settings");
    public string SearchPlaceholder => Loc.T("Main.SearchTemplates");
    public string DuplicateLabel => Loc.T("Main.Duplicate");
    public string SettingsTitleLabel => Loc.T("Settings.Title");
    public string ThemeLabel => Loc.T("Settings.Theme");
    public string LanguageLabel => Loc.T("Settings.Language");
    public string CheckForUpdatesLabel => Loc.T("Settings.CheckForUpdates");
    public string SignOutLabel => Loc.T("Settings.SignOut");
    public string RenewLabel => Loc.T("Settings.Renew");
    public string ExportPresetsLabel => Loc.T("Settings.ExportPresets");
    public string WatermarkLabel => Loc.T("Settings.Watermark");
    public string OpenFolderLabel => Loc.T("Toast.OpenFolder");
    public string CommandPalettePlaceholder => Loc.T("CommandPalette.Placeholder");

    partial void OnWatermarkEnabledChanged(bool value)
    {
        var appSettings = _settings.Load();
        appSettings.WatermarkEnabled = value;
        _settings.Save(appSettings);
        RenderNow();
    }

    partial void OnSnapToGridEnabledChanged(bool value)
    {
        var appSettings = _settings.Load();
        appSettings.SnapToGridEnabled = value;
        _settings.Save(appSettings);
    }

    [RelayCommand]
    private void ToggleSettings() => ShowSettings = !ShowSettings;

    private void UpdateLicenseCountdown()
    {
        var expires = _license.State.ExpiresAt;
        if (expires is null)
        {
            LicenseCountdownText = Loc.T("License.Lifetime");
            ShowRenewLink = false;
            return;
        }

        var remaining = expires.Value - DateTimeOffset.UtcNow;
        if (remaining.TotalDays < 1)
        {
            LicenseCountdownText = Loc.T("License.ExpiresToday");
        }
        else
        {
            var days = (int)Math.Ceiling(remaining.TotalDays);
            LicenseCountdownText = string.Format(Loc.T("License.DaysRemaining"), days);
        }
        ShowRenewLink = remaining.TotalDays <= 7;
    }

    // ---- Command palette ----------------------------------------------

    private void BuildCommandPaletteItems()
    {
        CommandPaletteItems.Clear();
        CommandPaletteItems.Add(new CommandPaletteItem("Export", () => Export()));
        CommandPaletteItems.Add(new CommandPaletteItem("Export all formats (PNG/JPG/PDF)", () => ExportAllFormats()));
        CommandPaletteItems.Add(new CommandPaletteItem("Reset fields to defaults", () => ResetToDefaults()));
        CommandPaletteItems.Add(new CommandPaletteItem("Clear all fields", () => ClearAll()));
        CommandPaletteItems.Add(new CommandPaletteItem("Toggle zoom", () => ToggleZoom()));
        CommandPaletteItems.Add(new CommandPaletteItem("Open settings", () => ShowSettings = true));
        CommandPaletteItems.Add(new CommandPaletteItem("Toggle theme (Light)", () => Theme = ThemePreference.Light));
        CommandPaletteItems.Add(new CommandPaletteItem("Toggle theme (Dark)", () => Theme = ThemePreference.Dark));
        CommandPaletteItems.Add(new CommandPaletteItem("Check for updates", () => _ = CheckForUpdatesManualAsync()));
        CommandPaletteItems.Add(new CommandPaletteItem("Duplicate current template", () => DuplicateTemplate()));
        FilterCommandPalette();
    }

    [RelayCommand]
    private void OpenCommandPalette()
    {
        CommandPaletteQuery = "";
        FilterCommandPalette();
        ShowCommandPalette = true;
    }

    [RelayCommand]
    private void CloseCommandPalette() => ShowCommandPalette = false;

    partial void OnCommandPaletteQueryChanged(string value) => FilterCommandPalette();

    private void FilterCommandPalette()
    {
        FilteredCommandPaletteItems.Clear();
        var query = CommandPaletteQuery.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? CommandPaletteItems
            : CommandPaletteItems.Where(c => c.Label.Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (var m in matches)
            FilteredCommandPaletteItems.Add(m);
    }

    [RelayCommand]
    private void RunCommandPaletteItem(CommandPaletteItem item)
    {
        item.Execute();
        ShowCommandPalette = false;
    }
}

public sealed class CommandPaletteItem
{
    public string Label { get; }
    public Action Execute { get; }
    public CommandPaletteItem(string label, Action execute)
    {
        Label = label;
        Execute = execute;
    }
}

/// <summary>Wraps an SKBitmap so it can travel through an EventArgs without a hard SkiaSharp
/// dependency on every subscriber; caller (View) is responsible for disposing it.</summary>
public sealed class SKBitmapHolder : IDisposable
{
    public SkiaSharp.SKBitmap Bitmap { get; }
    public SKBitmapHolder(SkiaSharp.SKBitmap bitmap) => Bitmap = bitmap;
    public void Dispose() => Bitmap.Dispose();
}
