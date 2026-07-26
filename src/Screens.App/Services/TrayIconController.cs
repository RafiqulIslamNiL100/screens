using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Screens.App.ViewModels;

namespace Screens.App.Services;

/// <summary>
/// System tray icon with a one-click "Export last template" menu item, so a
/// quick re-export doesn't require bringing the main window to front. Does
/// not replace the window's own export flow — same underlying command.
/// </summary>
public static class TrayIconController
{
    public static void Attach(Application app, MainViewModel mainVm)
    {
        var icon = new WindowIcon(new Bitmap(AssetLoader.Open(new System.Uri("avares://Screens/Assets/Icons/app.ico"))));

        var exportItem = new NativeMenuItem("Export current template");
        exportItem.Click += (_, _) => mainVm.ExportCommand.Execute(null);

        var showItem = new NativeMenuItem("Show Screens");
        showItem.Click += (_, _) =>
        {
            if (app.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { } window)
            {
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
            }
        };

        var menu = new NativeMenu { Items = { showItem, exportItem } };

        var trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "Screens",
            Menu = menu,
            IsVisible = true,
        };

        var icons = new TrayIcons { trayIcon };
        TrayIcon.SetIcons(app, icons);
    }
}
