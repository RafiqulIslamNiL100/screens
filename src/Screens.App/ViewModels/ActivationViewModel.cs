using System;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Screens.App.Services;

namespace Screens.App.ViewModels;

/// <summary>Activation key input: masked/grouped XXXX-XXXX-XXXX-XXXX with auto-dash and paste support.</summary>
public partial class ActivationViewModel : ViewModelBase
{
    private readonly LicenseService _license;

    [ObservableProperty] private string _keyInput = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    public event EventHandler? Activated;

    public ActivationViewModel(LicenseService license) => _license = license;

    partial void OnKeyInputChanged(string value)
    {
        var formatted = FormatKey(value);
        if (formatted != value)
        {
            KeyInput = formatted;
            return;
        }
        ActivateCommand.NotifyCanExecuteChanged();
    }

    public static string FormatKey(string raw)
    {
        var chars = new StringBuilder();
        foreach (var c in raw.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c))
                chars.Append(c);
        }
        if (chars.Length > 16)
            chars.Length = 16;

        var result = new StringBuilder();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
                result.Append('-');
            result.Append(chars[i]);
        }
        return result.ToString();
    }

    private bool CanActivate() => !IsBusy && KeyInput.Length == 19; // XXXX-XXXX-XXXX-XXXX

    [RelayCommand(CanExecute = nameof(CanActivate))]
    private async Task ActivateAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var outcome = await _license.RedeemAsync(KeyInput);
            switch (outcome)
            {
                case RedeemOutcome.Success:
                    Activated?.Invoke(this, EventArgs.Empty);
                    break;
                case RedeemOutcome.InvalidKey:
                    ErrorMessage = "That activation key isn't valid. Double-check and try again.";
                    break;
                case RedeemOutcome.AlreadyRedeemed:
                    ErrorMessage = "This key has already been redeemed by another account.";
                    break;
                case RedeemOutcome.Revoked:
                    ErrorMessage = "This key has been revoked.";
                    break;
                case RedeemOutcome.NoNetwork:
                    ErrorMessage = "Could not reach the server. Check your internet connection.";
                    break;
                default:
                    ErrorMessage = "Something went wrong activating this key. Please try again.";
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
