// Parrot for Windows. Derived from Parrot (GPL-3.0).
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Parrot.App.Services;
using Parrot.App.ViewModels;
using Parrot.App.Views;
using Parrot.Core.Diagnostics;
using Parrot.Core.Storage;

namespace Parrot.App;

public partial class App : Application
{
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        _services = new AppServices(AppPaths.Default());
        Log.Info("Parrot for Windows starting");
        var recovered = _services.Meetings.RecoverInterrupted();
        if (recovered > 0) Log.Info($"Recovered {recovered} interrupted meeting(s)");

        ThemeManager.Apply(_services.Settings.Theme);
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category == UserPreferenceCategory.General && _services.Settings.Theme == ThemePreference.System)
                Dispatcher.Invoke(() => ThemeManager.Apply(ThemePreference.System));
        };

        var vm = new MainViewModel(_services);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(e.Exception.Message, "Parrot", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
