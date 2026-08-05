using System.Windows;

namespace Virtua.Display;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\Virtua.Display";
    private Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        if (eventArgs.Args.Contains("--driver-status", StringComparer.OrdinalIgnoreCase))
        {
            var status = SudoVdaClient.Probe();
            Console.WriteLine($"{status.Kind}|{status.Message}");
            Shutdown((int)status.Kind);
            return;
        }
        if (eventArgs.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest.Run());
            return;
        }

        if (eventArgs.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(SmokeTest.Run());
            return;
        }

        if (eventArgs.Args.Contains("--smoke-window", StringComparer.OrdinalIgnoreCase))
        {
            ShowWindow(SmokeTest.CreateTestWindow());
            return;
        }

        StartupRegistration.RemoveLegacy();

        _singleInstanceMutex = TryAcquireSingleInstance(SingleInstanceName);
        if (_singleInstanceMutex is null)
        {
            Shutdown();
            return;
        }

        _mainWindow = new MainWindow();
        _mainWindow.SettingsRequested += ShowSettings;
        ShowWindow(
            _mainWindow,
            eventArgs.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase) &&
            _mainWindow.MinimizeToNotificationAreaEnabled);
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _settingsWindow?.Close();
        _settingsWindow = null;
        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(eventArgs);
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Show();
            _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_mainWindow!)
        {
            Owner = _mainWindow?.IsVisible == true ? _mainWindow : null,
            WindowStartupLocation = _mainWindow?.IsVisible == true
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    internal static Mutex? TryAcquireSingleInstance(string name)
    {
        var mutex = new Mutex(true, name, out var createdNew);
        if (createdNew)
            return mutex;

        mutex.Dispose();
        return null;
    }

    private void ShowWindow(Window window, bool hidden = false)
    {
        MainWindow = window;
        window.Closed += (_, _) =>
        {
            _settingsWindow?.Close();
            Shutdown();
        };
        if (hidden && window is MainWindow mainWindow)
            mainWindow.HideToNotificationArea();
        else
            window.Show();
    }
}
