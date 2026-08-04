using System.Windows;

namespace Virtua.Display;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\Virtua.Display";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

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

        _singleInstanceMutex = TryAcquireSingleInstance(SingleInstanceName);
        if (_singleInstanceMutex is null)
        {
            Shutdown();
            return;
        }

        var window = new MainWindow();
        ShowWindow(
            window,
            eventArgs.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase) &&
            window.MinimizeToNotificationAreaEnabled);
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(eventArgs);
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
        window.Closed += (_, _) => Shutdown();
        if (hidden && window is MainWindow mainWindow)
            mainWindow.HideToNotificationArea();
        else
            window.Show();
    }
}
