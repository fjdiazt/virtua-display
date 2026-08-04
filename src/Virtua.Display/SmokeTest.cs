using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace Virtua.Display;

internal static class SmokeTest
{
    private static readonly Guid MonitorGuid = new("8d6a8a70-67e9-4af0-9e57-0fcb401ca31b");

    internal static int Run()
    {
        var original = DisplayController.Capture();
        var mode = original.Displays.Single(display => display.Primary).Mode;
        var errors = new List<string>();
        SudoVdaClient? driver = null;
        CancellationTokenSource? watchdogCancellation = null;
        Task watchdogTask = Task.CompletedTask;
        WindowRouter? router = null;
        Process? child = null;
        var added = false;

        try
        {
            driver = SudoVdaClient.Open();
            var watchdog = driver.GetWatchdog();
            var addedDisplay = driver.Add(mode, MonitorGuid);
            added = true;
            watchdogCancellation = new CancellationTokenSource();
            watchdogTask = RunWatchdogAsync(driver, watchdog.Timeout, watchdogCancellation.Token);

            var deviceName = DisplayController.WaitForDisplayAsync(
                    addedDisplay,
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Console.WriteLine($"SudoVDA ADD: LUID=0x{addedDisplay.AdapterLuid:X16}, target={addedDisplay.TargetId}, resolved={deviceName}.");
            var bounds = DisplayController.PlaceAndSetPrimary(deviceName, mode, false);
            router = WindowRouter.Start(bounds, message => errors.Add(message));

            child = StartTestWindow();
            var window = WaitForWindow(child, TimeSpan.FromSeconds(5));
            var routedScreen = WaitForScreen(window, deviceName, TimeSpan.FromSeconds(5));
            if (!string.Equals(routedScreen, deviceName, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Window routed to {routedScreen}, expected {deviceName}.");

            DisplayController.MakePrimary(deviceName);

            var primary = DisplayController.Capture().Displays.Single(display => display.Primary);
            if (!string.Equals(primary.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Primary is {primary.DeviceName}, expected {deviceName}.");

            Console.WriteLine($"Live display active: {deviceName}, {mode}; routed child screen: {routedScreen}.");
        }
        catch (Exception exception)
        {
            errors.Add(exception.Message);
        }
        finally
        {
            CloseTestWindow(child, errors);
            Try(() => router?.Dispose(), "stop routing", errors);
            Try(() => DisplayController.Restore(original), "restore topology", errors);

            watchdogCancellation?.Cancel();
            Try(() => watchdogTask.GetAwaiter().GetResult(), "stop watchdog", errors);
            if (added && driver is not null)
                Try(() => driver.Remove(MonitorGuid), "remove virtual display", errors);

            watchdogCancellation?.Dispose();
            driver?.Dispose();
        }

        try
        {
            if (!WaitForTopology(original, TimeSpan.FromSeconds(5)))
                errors.Add("Original display topology was not restored exactly.");
        }
        catch (Exception exception)
        {
            errors.Add($"verify restored topology: {exception.Message}");
        }

        if (errors.Count == 0)
        {
            Console.WriteLine("Live smoke test passed; virtual display removed and topology restored.");
            return 0;
        }

        foreach (var error in errors)
            Console.Error.WriteLine($"SMOKE FAIL: {error}");
        return 1;
    }

    internal static Window CreateTestWindow() => new()
    {
        Title = "Virtua Display Smoke Window",
        Width = 640,
        Height = 480,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = 40,
        Top = 40,
        Background = Brushes.Black
    };

    private static Process StartTestWindow()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        return Process.Start(new ProcessStartInfo(executable, "--smoke-window")
        {
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not start smoke window process.");
    }

    private static IntPtr WaitForWindow(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited)
                throw new InvalidOperationException($"Smoke window exited with code {process.ExitCode}.");
            if (process.MainWindowHandle != IntPtr.Zero)
                return process.MainWindowHandle;
            Thread.Sleep(50);
        }

        throw new TimeoutException("Smoke window did not expose a top-level HWND.");
    }

    private static string WaitForScreen(IntPtr window, string expectedDevice, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var actual = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            actual = MonitorDeviceName(window);
            if (string.Equals(actual, expectedDevice, StringComparison.OrdinalIgnoreCase))
                return actual;
            Thread.Sleep(50);
        }

        return actual;
    }

    private static string MonitorDeviceName(IntPtr window)
    {
        var monitor = MonitorFromWindow(window, 0);
        if (monitor == IntPtr.Zero)
            return string.Empty;

        var info = new MonitorInfoEx
        {
            Size = (uint)Marshal.SizeOf<MonitorInfoEx>(),
            DeviceName = string.Empty
        };
        return GetMonitorInfo(monitor, ref info) ? info.DeviceName : string.Empty;
    }

    private static async Task RunWatchdogAsync(
        SudoVdaClient driver,
        uint timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (timeoutSeconds == 0)
            return;

        var delay = TimeSpan.FromMilliseconds(Math.Max(250, timeoutSeconds * 1000d / 3d));
        try
        {
            while (true)
            {
                await Task.Delay(delay, cancellationToken);
                driver.Ping();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }


    private static bool WaitForTopology(DisplaySnapshot expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (TopologyMatches(expected, DisplayController.Capture()))
                return true;

            Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);

        return false;
    }
    private static bool TopologyMatches(DisplaySnapshot expected, DisplaySnapshot actual)
    {
        if (expected.Displays.Count != actual.Displays.Count)
            return false;

        return expected.Displays.All(expectedDisplay =>
            actual.Displays.Any(actualDisplay =>
                string.Equals(expectedDisplay.DeviceName, actualDisplay.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                expectedDisplay.Position == actualDisplay.Position &&
                expectedDisplay.Mode == actualDisplay.Mode &&
                expectedDisplay.Primary == actualDisplay.Primary));
    }

    private static void CloseTestWindow(Process? process, ICollection<string> errors)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(2000))
                    process.Kill(true);
            }
        }
        catch (Exception exception)
        {
            errors.Add($"close smoke window: {exception.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void Try(Action action, string operation, ICollection<string> errors)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Add($"{operation}: {exception.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        internal uint Size;
        internal NativeRect Monitor;
        internal NativeRect Work;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetMonitorInfoW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
