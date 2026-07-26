using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Screens.App.Models;
using Screens.App.Services;

namespace Screens.App.ViewModels;

public enum ShellScreen { Splash, Auth, Activation, Main }

/// <summary>
/// Owns navigation between the four top-level screens. Never hides or
/// minimises the window as a side effect (spec section 5) — it only ever
/// swaps which view is displayed inside the single MainWindow.
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    private readonly AuthService _auth;
    private readonly LicenseService _license;

    [ObservableProperty] private ShellScreen _screen = ShellScreen.Splash;
    [ObservableProperty] private bool _showOfflineGraceBanner;

    public AuthViewModel AuthViewModel { get; }
    public ActivationViewModel ActivationViewModel { get; }
    public MainViewModel MainViewModel { get; }

    public ShellViewModel(AuthService auth, LicenseService license, AuthViewModel authVm, ActivationViewModel activationVm, MainViewModel mainVm)
    {
        _auth = auth;
        _license = license;
        AuthViewModel = authVm;
        ActivationViewModel = activationVm;
        MainViewModel = mainVm;

        AuthViewModel.Authenticated += async (_, _) => await AfterAuthAsync();
        ActivationViewModel.Activated += (_, _) => Screen = ShellScreen.Main;
        _license.StateChanged += OnLicenseStateChanged;
    }

    public async Task InitializeAsync()
    {
        var restored = await _auth.TryRestoreSessionAsync();
        if (!restored)
        {
            Screen = ShellScreen.Auth;
            return;
        }

        await AfterAuthAsync();
    }

    private async Task AfterAuthAsync()
    {
        await _license.InitializeAsync();
        Screen = _license.State.IsActive ? ShellScreen.Main : ShellScreen.Activation;
        if (Screen == ShellScreen.Main)
            await MainViewModel.CheckForUpdatesOnStartupAsync();
    }

    private void OnLicenseStateChanged(LicenseState state)
    {
        ShowOfflineGraceBanner = _license.InOfflineGrace;

        if (!state.IsActive && Screen == ShellScreen.Main)
        {
            // Re-lock immediately on revocation, without minimising or closing the window.
            Screen = ShellScreen.Activation;
        }
    }

    public async void SignOut()
    {
        await _auth.SignOutAsync();
        Screen = ShellScreen.Auth;
    }
}
