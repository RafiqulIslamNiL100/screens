using Avalonia;
using Avalonia.Win32;
using System;
using System.Collections.Generic;

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
        .With(new Win32PlatformOptions
        {
            // Try hardware first, but fall back to software instead of a
            // black window on machines/VMs whose GPU driver can't satisfy
            // the ANGLE/Direct3D path (observed under Wine's software GL
            // during Wine/Xvfb acceptance testing).
            RenderingMode = new List<Win32RenderingMode>
            {
                Win32RenderingMode.Software,
            },
        })
        .WithInterFont()
        .LogToTrace();
}
