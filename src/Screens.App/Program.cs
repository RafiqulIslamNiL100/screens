using Avalonia;
using System;

namespace Screens.App;

internal static class Program
{
    // Avalonia entry point. Do not run Avalonia code before AppMain is invoked
    // (custom App.xaml.cs services are constructed lazily inside App).
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
