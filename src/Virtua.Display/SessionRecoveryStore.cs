using Microsoft.Win32;
using System.Text.Json;

namespace Virtua.Display;

internal static class SessionRecoveryStore
{
    internal const string ValueName = "ActiveSession";

    internal static SessionRecoveryState? Load(string path = UserSettingsStore.DefaultPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key?.GetValue(ValueName) is not string json)
                return null;

            var state = JsonSerializer.Deserialize<SessionRecoveryState>(json);
            if (state is not null && IsValid(state))
                return state;
        }
        catch
        {
        }

        Clear(path);
        return null;
    }

    internal static void Save(
        SessionRecoveryState state,
        string path = UserSettingsStore.DefaultPath)
    {
        if (!IsValid(state))
            throw new ArgumentException("Recovery state is invalid.", nameof(state));

        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue(ValueName, JsonSerializer.Serialize(state), RegistryValueKind.String);
    }

    internal static void Clear(string path = UserSettingsStore.DefaultPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static bool IsValid(SessionRecoveryState state) =>
        DisplayController.IsSupported(state.Mode) &&
        state.Snapshot.Displays.Count > 0 &&
        state.Snapshot.Displays.Count(display => display.Primary) == 1 &&
        state.Snapshot.Displays.All(display =>
            !string.IsNullOrWhiteSpace(display.DeviceName) &&
            DisplayController.IsSupported(display.Mode));
}