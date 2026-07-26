using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Screens.App.Models;

namespace Screens.App.ViewModels;

public partial class FieldEditorItemViewModel : ViewModelBase
{
    public TemplateField Field { get; }

    [ObservableProperty] private string _value;

    public event EventHandler? ValueChanged;

    public FieldEditorItemViewModel(TemplateField field)
    {
        Field = field;
        _value = field.Default;
    }

    public int CharacterCount => Value.Length;
    public bool HasMaxLength => Field.MaxLength.HasValue;
    public string CounterText => Field.MaxLength.HasValue ? $"{Value.Length} / {Field.MaxLength.Value}" : "";

    partial void OnValueChanged(string value)
    {
        if (Field.MaxLength.HasValue && value.Length > Field.MaxLength.Value)
        {
            Value = value[..Field.MaxLength.Value];
            return;
        }
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(CounterText));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        Value = Field.Default;
    }

    public void Clear()
    {
        Value = "";
    }
}
