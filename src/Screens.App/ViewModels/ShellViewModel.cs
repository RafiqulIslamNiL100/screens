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
    private readonly PremiumAccessService _premiumAccess;

    [ObservableProperty] private ShellScreen _screen = ShellScreen.Splash;
    [ObservableProperty] private bool _showOfflineGraceBanner;

    public AuthViewModel AuthViewModel { get; }
    public ActivationViewModel ActivationViewModel { get; }
    public MainViewModel MainViewModel { get; }

    public ShellViewModel(AuthService auth, LicenseService license, PremiumAccessService premiumAccess, AuthViewModel authVm, ActivationViewModel activationVm, MainViewModel mainVm)
    {
        _auth = auth;
        _license = license;
        _premiumAccess = premiumAccess;
        AuthViewModel = authVm;
        ActivationViewModel = activationVm;
        MainViewModel = mainVm;

        AuthViewModel.Authenticated += async (_, _) =>
        {
            // This is an async-void event handler: an uncaught exception
            // here doesn't propagate anywhere useful, it just vanishes and
            // leaves the UI stuck wherever it was. Never let that happen.
            try { await AfterAuthAsync(); }
            catch { Screen = ShellScreen.Activation; }
        };
        ActivationViewModel.Activated += (_, _) => Screen = ShellScreen.Main;
        _license.StateChanged += OnLicenseStateChanged;
    }

    public async Task InitializeAsync()
    {
        // Called fire-and-forget from App.axaml.cs (there's nothing to await
        // it from at that point — the window is already showing). An
        // uncaught exception anywhere below must never leave the app
        // stranded on the splash screen forever, so this is the one place
        // allowed to catch everything.
        try
        {
            var restored = await _auth.TryRestoreSessionAsync();
            if (!restored)
            {
                Screen = ShellScreen.Auth;
                return;
            }

            await AfterAuthAsync();
        }
        catch
        {
            Screen = ShellScreen.Auth;
        }
    }

    private async Task AfterAuthAsync()
    {
        await _license.InitializeAsync();
        // Independent of app-license activation on purpose — a user with no premium key
        // yet should still land on Main once the app itself is licensed, so this must never
        // gate Screen the way _license.State.IsActive does.
        await _premiumAccess.InitializeAsync();
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
