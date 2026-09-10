using System.Globalization;
using System.IO;
using System.Text;

namespace Virtua.Display;

internal static class AppLog
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Virtua Display",
        "Logs",
        "virtua-display.log");
    private static volatile bool _enabled;

    internal static void Start(string message)
    {
        _enabled = true;
        Info(message);
    }

    internal static void Info(string message)
    {
        if (_enabled)
            Write(LogPath, "INFO", message, null);
    }

    internal static void Error(string message, Exception? exception = null)
    {
        if (_enabled)
            Write(LogPath, "ERROR", message, exception);
    }

    internal static void Write(string path, string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length >= MaxFileBytes)
                {
                    var previousPath = Path.Combine(
                        Path.GetDirectoryName(path)!,
                        $"{Path.GetFileNameWithoutExtension(path)}.previous{Path.GetExtension(path)}");
                    File.Move(path, previousPath, true);
                }

                var entry = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{DateTimeOffset.Now:O} | {level} | pid={Environment.ProcessId} | tid={Environment.CurrentManagedThreadId} | {message}{Environment.NewLine}");
                if (exception is not null)
                    entry += exception + Environment.NewLine;
                File.AppendAllText(path, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
