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
using SkiaSharp;

namespace Screens.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly TemplateService _templates;
    private readonly RenderService _render;
    private readonly SettingsService _settings;
    private readonly UpdateService _update;
    private readonly FontRegistry _fonts;
    private readonly LicenseService _license;
    private readonly OcrTemplateService _ocr;
    public LocalizationService Loc { get; }

    private CancellationTokenSource? _debounceCts;
    private List<TemplateManifest> _allTemplates = new();
    private bool _isApplyingDraft;

    [ObservableProperty] private TemplateManifest? _selectedTemplate;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private bool _isZoomToFit = true;
    [ObservableProperty] private double _zoomLevel = 1.0;
    [ObservableProperty] private double _fitScale = 1.0;
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
    [ObservableProperty] private bool _watermarkEnabled;
    [ObservableProperty] private bool _snapToGridEnabled = true;
    [ObservableProperty] private string? _licenseCountdownText;
    [ObservableProperty] private bool _showRenewLink;
    [ObservableProperty] private bool _showCommandPalette;
    [ObservableProperty] private string _commandPaletteQuery = "";
    [ObservableProperty] private string _newPresetName = "";
    [ObservableProperty] private string _newPresetFormat = "png";

    // ---- Build-your-own-template mode --------------------------------
    // The raw photo is never decoded/re-encoded here — only ever File.Copy'd, byte-for-byte,
    // when the template is saved (see SaveBuildTemplate) — so nothing about it is ever
    // recompressed or loses a pixel versus what the user imported.
    [ObservableProperty] private bool _isBuildingTemplate;
    [ObservableProperty] private string? _buildPhotoPath;
    [ObservableProperty] private int _buildPhotoWidth;
    [ObservableProperty] private int _buildPhotoHeight;
    [ObservableProperty] private bool _buildDrawModeIsPhoto = true;

    /// <summary>The inverse of <see cref="BuildDrawModeIsPhoto"/>, so the "Text placeholder"
    /// radio button has something to two-way bind to without a non-reversible negation binding.</summary>
    public bool BuildDrawModeIsText
    {
        get => !BuildDrawModeIsPhoto;
        set => BuildDrawModeIsPhoto = !value;
    }

    partial void OnBuildDrawModeIsPhotoChanged(bool value) => OnPropertyChanged(nameof(BuildDrawModeIsText));

    public ObservableCollection<TemplateField> BuildFields { get; } = new();

    public void StartBuildTemplate(string photoPath, int width, int height)
    {
        BuildPhotoPath = photoPath;
        BuildPhotoWidth = width;
        BuildPhotoHeight = height;
        BuildFields.Clear();
        IsBuildingTemplate = true;
    }

    /// <summary>Called by the view once the user finishes dragging out a rectangle on the raw
    /// photo — coordinates are already in photo-pixel space. Anything smaller than a few pixels
    /// is treated as an accidental click, not a real region.</summary>
    public void AddBuildRegion(double x, double y, double width, double height)
    {
        if (width < 6 || height < 6)
            return;

        var index = BuildFields.Count + 1;
        var isPhoto = BuildDrawModeIsPhoto;
        BuildFields.Add(new TemplateField
        {
            Id = isPhoto ? $"photo{index}" : $"text{index}",
            Type = isPhoto ? "image" : "text",
            Label = isPhoto ? $"Photo {index}" : $"Text {index}",
            Default = isPhoto ? "" : "Edit me",
            Placeholder = isPhoto ? "Choose a photo" : "Enter text",
            Box = new FieldBox { X = x, Y = y, Width = width, Height = height },
            Align = "left",
            VerticalAlign = "middle",
            // Color and Weight are placeholders — SaveBuildTemplate overwrites both per text
            // field by sampling the actual pixels under this box, right before erasing them.
            Font = new FieldFont { Family = "Inter", Size = Math.Max(12, height * 0.72), Weight = "Regular" },
            Color = "#1A1028",
            AutoShrink = true,
            UserEditableColor = true,
            UserEditableSize = true,
        });
    }

    [RelayCommand]
    private void RemoveBuildRegion(TemplateField? field)
    {
        if (field is not null)
            BuildFields.Remove(field);
    }

    [RelayCommand]
    private void CancelBuildTemplate()
    {
        IsBuildingTemplate = false;
        BuildFields.Clear();
        BuildPhotoPath = null;
    }

    [RelayCommand]
    private void SaveBuildTemplate()
    {
        if (BuildPhotoPath is null || BuildFields.Count == 0)
            return;

        var newId = $"custom-{DateTime.Now:yyyyMMddHHmmss}";
        var destDir = _settings.TemplatesDirectory;
        Directory.CreateDirectory(destDir);

        var textFields = BuildFields.Where(f => f.Type == "text").ToList();
        string imageName;

        if (textFields.Count == 0)
        {
            // No text regions marked — nothing needs to be erased, so the photo is copied
            // byte-for-byte, exactly as imported, at whatever resolution/format it already was.
            var ext = Path.GetExtension(BuildPhotoPath);
            if (string.IsNullOrEmpty(ext))
                ext = ".png";
            imageName = $"{newId}{ext}";
            File.Copy(BuildPhotoPath, Path.Combine(destDir, imageName), overwrite: true);
        }
        else
        {
            // At least one text region: sample its original color (and, relative to the other
            // text regions here, whether it reads bolder than them) before erasing it — erasing
            // first would sample the erased fill instead of the real text. Erasing is what makes
            // typing a replacement value actually *replace* the baked-in text instead of drawing
            // new text over/next to the old. This does mean the file is re-encoded as PNG
            // (lossless) rather than byte-copied, since its pixels are genuinely being edited.
            imageName = $"{newId}.png";
            using var bitmap = SKBitmap.Decode(BuildPhotoPath)
                ?? throw new InvalidOperationException("Could not read the imported photo.");

            var boxes = textFields.Select(f => new SKRectI(
                (int)f.Box.X, (int)f.Box.Y,
                (int)(f.Box.X + f.Box.Width), (int)(f.Box.Y + f.Box.Height))).ToList();

            var sampled = boxes.Select(b => PhotoRegionEraser.SampleTextColorAndDensity(bitmap, b)).ToList();
            var medianInk = sampled.Select(s => s.InkRatio).OrderBy(v => v).ElementAt(sampled.Count / 2);

            for (var i = 0; i < textFields.Count; i++)
            {
                textFields[i].Color = sampled[i].Color;
                textFields[i].Font.Weight = medianInk > 0 && sampled[i].InkRatio > medianInk * 1.3 ? "Bold" : "Regular";
                PhotoRegionEraser.Erase(bitmap, boxes[i]);
            }

            using var fs = File.Create(Path.Combine(destDir, imageName));
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            data.SaveTo(fs);
        }

        var manifest = new TemplateManifest
        {
            Id = newId,
            Name = $"My Template {DateTime.Now:MMM d, HH:mm}",
            Category = "My Templates",
            Image = imageName,
            Canvas = new CanvasSize { Width = BuildPhotoWidth, Height = BuildPhotoHeight },
            Fields = BuildFields.ToList(),
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(destDir, $"{newId}.json"), json);

        IsBuildingTemplate = false;
        BuildFields.Clear();
        BuildPhotoPath = null;

        LoadTemplates(newId);
        ToastMessage = "Template saved — adjust font, size, or color for any field below.";
        ShowToast = true;
    }

    public ObservableCollection<FieldEditorItemViewModel> Fields { get; } = new();
    public ObservableCollection<TemplateLoadWarning> Warnings { get; } = new();
    public ObservableCollection<ExportPreset> ExportPresets { get; } = new();
    public ObservableCollection<CommandPaletteItem> CommandPaletteItems { get; } = new();
    public ObservableCollection<CommandPaletteItem> FilteredCommandPaletteItems { get; } = new();

    public event EventHandler<(SKBitmapHolder Bitmap, string DefaultFileName, ExportFormat Format)>? ExportRequested;
    public event EventHandler<SKBitmapHolder>? ClipboardExportRequested;
    public event EventHandler<ThemePreference>? ThemeChanged;

    public MainViewModel(TemplateService templates, RenderService render, SettingsService settings, UpdateService update, FontRegistry fonts, LicenseService license, LocalizationService loc, OcrTemplateService ocr)
    {
        _templates = templates;
        _render = render;
        _settings = settings;
        _update = update;
        _fonts = fonts;
        _license = license;
        _ocr = ocr;
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

        SelectedTemplate = _allTemplates.FirstOrDefault(t => t.Id == lastTemplateId) ?? _allTemplates.FirstOrDefault();
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

    private const double MinZoom = 0.1;
    private const double MaxZoom = 4.0;

    public double EffectiveZoom => IsZoomToFit ? FitScale : ZoomLevel;
    public string ZoomPercentText => IsZoomToFit ? "Fit" : $"{(int)Math.Round(ZoomLevel * 100)}%";

    partial void OnIsZoomToFitChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveZoom));
        OnPropertyChanged(nameof(ZoomPercentText));
    }

    partial void OnZoomLevelChanged(double value)
    {
        OnPropertyChanged(nameof(EffectiveZoom));
        OnPropertyChanged(nameof(ZoomPercentText));
    }

    /// <summary>Set by the View whenever the canvas viewport is resized, so "Fit" tracks the window.</summary>
    public void SetFitScale(double scale)
    {
        if (scale <= 0)
            return;
        FitScale = scale;
        if (IsZoomToFit)
            OnPropertyChanged(nameof(EffectiveZoom));
    }

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(MaxZoom, EffectiveZoom * 1.25);
        IsZoomToFit = false;
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(MinZoom, EffectiveZoom * 0.8);
        IsZoomToFit = false;
    }

    [RelayCommand]
    private void ZoomToFitAction() => IsZoomToFit = true;

    [RelayCommand]
    private void ZoomReset()
    {
        ZoomLevel = 1.0;
        IsZoomToFit = false;
    }

    /// <summary>Ctrl+scroll on the canvas — delta is the wheel's raw Y delta (positive = zoom in).</summary>
    public void ZoomByWheel(double delta)
    {
        var factor = delta > 0 ? 1.1 : 1 / 1.1;
        ZoomLevel = Math.Clamp(EffectiveZoom * factor, MinZoom, MaxZoom);
        IsZoomToFit = false;
    }

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

    [ObservableProperty] private bool _isScanningImage;
    [ObservableProperty] private string? _scanImageError;

    public async Task ScanImageAsync(string sourceImagePath)
    {
        IsScanningImage = true;
        ScanImageError = null;
        try
        {
            var newId = await _ocr.ScanAsync(sourceImagePath);
            LoadTemplates(newId);
            ToastMessage = "Scanned — review the detected fields (font size/color are best-effort, nudge them in the field editor if needed).";
            ShowToast = true;
        }
        catch (OcrTemplateService.ScanException ex)
        {
            ScanImageError = ex.Message;
        }
        catch (Exception ex)
        {
            ScanImageError = $"Couldn't scan this picture: {ex.Message}";
        }
        finally
        {
            IsScanningImage = false;
        }
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

    [ObservableProperty] private bool _isInstallingUpdate;
    [ObservableProperty] private string? _installUpdateError;

    public event EventHandler? ExitForUpdateRequested;

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (PendingUpdate is null || IsInstallingUpdate)
            return;

        IsInstallingUpdate = true;
        InstallUpdateError = null;
        try
        {
            var (started, error) = await _update.DownloadAndInstallAsync(PendingUpdate);
            if (!started)
            {
                InstallUpdateError = error ?? "Update failed for an unknown reason.";
                IsInstallingUpdate = false;
                return;
            }

            // The helper .cmd is now waiting on this process to exit before it
            // runs the installer silently and relaunches Screens.exe — until
            // we actually exit, that wait never ends and "restart with the
            // new version" never happens. This is the one legitimate reason
            // to close the app as a side effect of an in-app action (spec
            // section 5's "no hiding/minimising" rule is about accidental
            // side effects, not this deliberate, user-initiated handoff).
            ExitForUpdateRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            InstallUpdateError = $"Update failed: {ex.Message}";
            IsInstallingUpdate = false;
        }
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
    public string SettingsLabel => Loc.T("Main.Settings");
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
        CommandPaletteItems.Add(new CommandPaletteItem("Zoom in", () => ZoomIn()));
        CommandPaletteItems.Add(new CommandPaletteItem("Zoom out", () => ZoomOut()));
        CommandPaletteItems.Add(new CommandPaletteItem("Zoom to fit", () => ZoomToFitAction()));
        CommandPaletteItems.Add(new CommandPaletteItem("Zoom to 100%", () => ZoomReset()));
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
