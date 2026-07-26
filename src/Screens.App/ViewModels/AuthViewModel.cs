using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Screens.App.Services;

namespace Screens.App.ViewModels;

public partial class AuthViewModel : ViewModelBase
{
    private readonly AuthService _auth;
    public LocalizationService Loc { get; }

    [ObservableProperty] private bool _isSignUpMode;
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    public event EventHandler? Authenticated;

    public AuthViewModel(AuthService auth, LocalizationService loc)
    {
        _auth = auth;
        Loc = loc;
        Loc.LanguageChanged += () => OnPropertyChanged(string.Empty);
    }

    public string HeadingText => IsSignUpMode ? Loc.T("Auth.SignUpHeading") : Loc.T("Auth.SignInHeading");
    public string SubmitText => IsSignUpMode ? Loc.T("Auth.SignUp") : Loc.T("Auth.SignIn");
    public string ToggleText => IsSignUpMode ? Loc.T("Auth.ToggleToSignIn") : Loc.T("Auth.ToggleToSignUp");
    public string EmailLabel => Loc.T("Auth.Email");
    public string PasswordLabel => Loc.T("Auth.Password");

    [RelayCommand]
    private void ToggleMode()
    {
        IsSignUpMode = !IsSignUpMode;
        ErrorMessage = null;
        OnPropertyChanged(nameof(HeadingText));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(ToggleText));
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = IsSignUpMode
                ? await _auth.SignUpAsync(Email.Trim(), Password)
                : await _auth.SignInAsync(Email.Trim(), Password);

            if (result.Success)
                Authenticated?.Invoke(this, EventArgs.Empty);
            else
                ErrorMessage = result.ErrorMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSubmit() => !IsBusy && Email.Contains('@') && Password.Length >= 6;

    partial void OnEmailChanged(string value) => SubmitCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => SubmitCommand.NotifyCanExecuteChanged();
}
