using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Screens.App.Models;

namespace Screens.App.ViewModels;

public partial class FieldEditorItemViewModel : ViewModelBase
{
    public TemplateField Field { get; }

    [ObservableProperty] private string _value;
    [ObservableProperty] private string _colorOverride;
    [ObservableProperty] private double _sizeOverride;

    public event EventHandler? ValueChanged;

    public FieldEditorItemViewModel(TemplateField field)
    {
        Field = field;
        _value = field.Default;
        _colorOverride = field.Color;
        _sizeOverride = field.Font.Size;
    }

    public bool IsQr => Field.Type == "qr";
    public bool IsImage => Field.Type == "image";
    public bool HasPhoto => IsImage && !string.IsNullOrEmpty(Value);
    public bool ShowColorPicker => Field.UserEditableColor && !IsQr && !IsImage;
    public bool ShowSizePicker => Field.UserEditableSize && !IsQr && !IsImage;

    public int CharacterCount => Value.Length;
    public bool HasMaxLength => Field.MaxLength.HasValue;
    public string CounterText => Field.MaxLength.HasValue ? $"{Value.Length} / {Field.MaxLength.Value}" : "";

    /// <summary>
    /// Rough heuristic (not the render path itself — RenderService is the
    /// single source of truth for actual shrinking) so the field editor can
    /// warn before the user exports, not after.
    /// </summary>
    public bool WillShrink
    {
        get
        {
            if (IsQr || IsImage || !Field.AutoShrink || Value.Length == 0)
                return false;
            var approxCharWidth = Field.Font.Size * 0.55;
            var charsPerLine = Math.Max(1, (int)(Field.Box.Width / approxCharWidth));
            if (!Field.Multiline)
                return Value.Length > charsPerLine;

            var lineCapacity = Math.Max(1, (int)(Field.Box.Height / (Field.Font.Size * Field.LineHeight)));
            var estimatedLines = Math.Max(1, (int)Math.Ceiling((double)Value.Length / charsPerLine));
            return estimatedLines > lineCapacity;
        }
    }

    partial void OnValueChanged(string value)
    {
        if (Field.MaxLength.HasValue && value.Length > Field.MaxLength.Value)
        {
            Value = value[..Field.MaxLength.Value];
            return;
        }
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(WillShrink));
        OnPropertyChanged(nameof(HasPhoto));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnColorOverrideChanged(string value)
    {
        Field.RuntimeColorOverride = string.IsNullOrWhiteSpace(value) ? null : value;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSizeOverrideChanged(double value)
    {
        Field.RuntimeSizeOverride = value > 0 ? value : null;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        Value = Field.Default;
        ColorOverride = Field.Color;
        SizeOverride = Field.Font.Size;
    }

    public void Clear()
    {
        Value = "";
    }
}
