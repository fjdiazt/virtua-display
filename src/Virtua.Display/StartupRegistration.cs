using Microsoft.Win32;
using System.IO;

namespace Virtua.Display;

internal static class StartupRegistration
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SudoVDA GUI";

    internal static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('"'))
            throw new ArgumentException("Executable path cannot contain quotes.", nameof(executablePath));

        var command = $"\"{Path.GetFullPath(executablePath)}\" --startup";
        if (command.Length > 260)
            throw new InvalidOperationException("Startup command exceeds Windows Run-key limit.");
        return command;
    }

    internal static bool IsEnabled(
        string? executablePath = null,
        string registryPath = RunPath,
        string valueName = ValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            return string.Equals(
                key?.GetValue(valueName) as string,
                BuildCommand(CurrentExecutable(executablePath)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static void SetEnabled(
        bool enabled,
        string? executablePath = null,
        string registryPath = RunPath,
        string valueName = ValueName)
    {
        if (!enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
            return;
        }

        using var writableKey = Registry.CurrentUser.CreateSubKey(registryPath);
        writableKey.SetValue(
            valueName,
            BuildCommand(CurrentExecutable(executablePath)),
            RegistryValueKind.String);
    }

    private static string CurrentExecutable(string? executablePath) =>
        executablePath ?? Environment.ProcessPath ??
        throw new InvalidOperationException("Current executable path is unavailable.");
}
