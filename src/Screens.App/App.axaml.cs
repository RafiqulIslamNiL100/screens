using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Screens.App.Services;
using Screens.App.ViewModels;
using Screens.App.Views;

namespace Screens.App;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var config = AppConfig.Load();
            var settings = new SettingsService();
            var auth = new AuthService(config, settings);
            var license = new LicenseService(auth, config, settings);
            var templates = new TemplateService(settings);
            var fonts = new FontRegistry();
            var render = new RenderService(fonts);
            var update = new UpdateService(config);

            var authVm = new AuthViewModel(auth);
            var activationVm = new ActivationViewModel(license);
            var mainVm = new MainViewModel(templates, render, settings, update, fonts);
            var shellVm = new ShellViewModel(auth, license, authVm, activationVm, mainVm);

            var appSettings = settings.Load();
            ApplyTheme(appSettings.Theme);
            mainVm.ThemeChanged += (_, theme) => ApplyTheme(theme);

            var window = new MainWindow { DataContext = shellVm };
            desktop.MainWindow = window;

            _ = shellVm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(ThemePreference theme)
    {
        RequestedThemeVariant = theme switch
        {
            ThemePreference.Dark => ThemeVariant.Dark,
            ThemePreference.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }
}
