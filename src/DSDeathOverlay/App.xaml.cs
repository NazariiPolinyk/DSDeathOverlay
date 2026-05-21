using System;
using System.Windows;
using DSDeathOverlay.Logging;
using DSDeathOverlay.Services;
using DSDeathOverlay.Settings;

namespace DSDeathOverlay;

public partial class App : Application
{
    private FileLogger? _log;
    private DeathPoller? _poller;
    private SettingsStore? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _log = new FileLogger();
        _log.Log("=== DSDeathOverlay starting ===");

        _settings = SettingsStore.Load(_log);
        _poller = new DeathPoller(_log);

        var window = new MainWindow();
        window.Initialize(_poller, _settings);
        window.Show();

        _poller.Start();

        // Surface unhandled exceptions to the log instead of crashing silently.
        DispatcherUnhandledException += (s, ex) =>
        {
            _log.Log($"Dispatcher exception: {ex.Exception}");
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            _log.Log($"AppDomain unhandled exception: {ex.ExceptionObject}");
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _poller?.Dispose();
        _log?.Log("=== DSDeathOverlay exiting ===");
        _log?.Dispose();
        base.OnExit(e);
    }
}
