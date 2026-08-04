using Microsoft.Win32;
using System.Globalization;

namespace Virtua.Display;

internal readonly record struct ResolutionSize(uint Width, uint Height)
{
    internal string Key => $"{Width}x{Height}";

    public override string ToString() => $"{Width} x {Height}";
}

internal readonly record struct ResolutionAspectRatio(
    uint Numerator,
    uint Denominator,
    string? Label)
{
    internal double Value => (double)Numerator / Denominator;
    internal string Key => $"{Numerator}:{Denominator}";
    internal string FilterLabel => Label is null ? Key : $"{Key} ({Label})";
    internal string ResolutionLabel => Label is null ? Key : $"{Key} {Label}";
}

internal sealed record UserSettings(
    string Preset,
    uint Width,
    uint Height,
    uint RefreshHz,
    bool MakePrimary,
    bool RouteNewWindows,
    bool MinimizeToNotificationArea,
    bool CloseToNotificationArea = false)
{
    internal const string CopyPrimary = "CopyPrimary";
    internal const string Custom = "Custom";

    internal static UserSettings Defaults(DisplayMode primary)
    {
        var refresh = primary.RefreshHz is >= 1 and <= 500 ? primary.RefreshHz : 60;
        return new(CopyPrimary, primary.Width, primary.Height, refresh, true, true, false, false);
    }
}

internal static class ResolutionOptions
{
    private static readonly ResolutionAspectRatio[] KnownAspectRatios =
    [
        new(1, 1, "Square"),
        new(3, 2, "Classic"),
        new(4, 3, "Standard"),
        new(5, 3, "Wide"),
        new(5, 4, "Standard"),
        new(16, 9, "Wide"),
        new(16, 10, "Wide"),
        new(21, 9, "Ultrawide"),
        new(32, 9, "Super ultrawide")
    ];

    private static readonly uint[] StandardRates =
    [
        24, 25, 30, 48, 50, 60, 72, 75, 90, 100, 120, 144, 165, 240, 360, 480, 500
    ];

    internal static IReadOnlyList<ResolutionSize> DistinctSizes(IEnumerable<DisplayMode> modes) =>
        modes
            .Select(mode => new ResolutionSize(mode.Width, mode.Height))
            .Distinct()
            .OrderBy(size => SortOrder(AspectRatio(size)))
            .ThenBy(size => AspectRatio(size).Value)
            .ThenBy(size => size.Width)
            .ThenBy(size => size.Height)
            .ToArray();

    internal static ResolutionAspectRatio AspectRatio(ResolutionSize size)
    {
        var divisor = GreatestCommonDivisor(size.Width, size.Height);
        var exact = new ResolutionAspectRatio(size.Width / divisor, size.Height / divisor, null);
        var nearest = KnownAspectRatios.MinBy(candidate =>
            Math.Abs(exact.Value - candidate.Value) / candidate.Value);
        return Math.Abs(exact.Value - nearest.Value) / nearest.Value <= 0.03
            ? nearest
            : exact;
    }

    internal static IReadOnlyList<ResolutionAspectRatio> AspectRatios(
        IEnumerable<ResolutionSize> sizes) =>
        sizes
            .Select(AspectRatio)
            .Distinct()
            .OrderBy(SortOrder)
            .ThenBy(value => value.Value)
            .ThenBy(value => value.Numerator)
            .ThenBy(value => value.Denominator)
            .ToArray();

    private static int SortOrder(ResolutionAspectRatio ratio)
    {
        var index = Array.IndexOf(KnownAspectRatios, ratio);
        return index < 0 ? int.MaxValue : index;
    }

    private static uint GreatestCommonDivisor(uint left, uint right)
    {
        while (right != 0)
            (left, right) = (right, left % right);
        return left;
    }

    internal static IReadOnlyList<uint> RefreshRates(uint primaryRefresh) =>
        StandardRates
            .Append(primaryRefresh)
            .Where(rate => rate is >= 1 and <= 500)
            .Distinct()
            .Order()
            .ToArray();

    internal static bool TryParseMode(
        string widthText,
        string heightText,
        uint refreshHz,
        out DisplayMode mode,
        out string widthError,
        out string heightError)
    {
        var widthValid = TryParseDimension(
            widthText, 640, 7680, "Width", out var width, out widthError);
        var heightValid = TryParseDimension(
            heightText, 480, 4320, "Height", out var height, out heightError);
        mode = new DisplayMode(width, height, refreshHz);
        return widthValid && heightValid && DisplayController.IsSupported(mode);
    }

    internal static bool TryParsePreset(string value, out ResolutionSize size)
    {
        var parts = value.Split('x');
        if (parts.Length == 2 &&
            uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) &&
            uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height))
        {
            size = new ResolutionSize(width, height);
            return true;
        }

        size = default;
        return false;
    }

    private static bool TryParseDimension(
        string text,
        uint minimum,
        uint maximum,
        string name,
        out uint value,
        out string error)
    {
        var valid = uint.TryParse(
                        text.Trim(),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out value) &&
                    value >= minimum &&
                    value <= maximum;
        error = valid ? string.Empty : $"{name} must be {minimum}–{maximum}.";
        return valid;
    }
}

internal static class UserSettingsStore
{
    internal const string DefaultPath = @"Software\VRPrivacy";

    internal static UserSettings Load(DisplayMode primary, string path = DefaultPath)
    {
        var defaults = UserSettings.Defaults(primary);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key is null)
                return defaults;

            var makePrimary = ReadBoolean(key, "MakePrimary", true);
            var routeNewWindows = ReadBoolean(key, "RouteNewWindows", true);
            var minimizeToNotificationArea = ReadBoolean(key, "MinimizeToNotificationArea", false);
            var closeToNotificationArea = ReadBoolean(key, "CloseToNotificationArea", false);
            var preset = key.GetValue("Preset") as string ?? UserSettings.CopyPrimary;
            var width = ReadUInt(key, "Width", defaults.Width);
            var height = ReadUInt(key, "Height", defaults.Height);
            var refresh = ReadUInt(key, "RefreshHz", defaults.RefreshHz);

            var presetValid = preset is UserSettings.CopyPrimary or UserSettings.Custom;
            if (ResolutionOptions.TryParsePreset(preset, out var presetSize))
                presetValid = presetSize == new ResolutionSize(width, height);

            var modeValid = ResolutionOptions.TryParseMode(
                width.ToString(CultureInfo.InvariantCulture),
                height.ToString(CultureInfo.InvariantCulture),
                refresh,
                out _,
                out _,
                out _);
            if (!presetValid ||
                !modeValid ||
                !ResolutionOptions.RefreshRates(primary.RefreshHz).Contains(refresh))
            {
                return defaults with
                {
                    MakePrimary = makePrimary,
                    RouteNewWindows = routeNewWindows,
                    MinimizeToNotificationArea = minimizeToNotificationArea,
                    CloseToNotificationArea = closeToNotificationArea
                };
            }

            return new(
                preset, width, height, refresh,
                makePrimary, routeNewWindows, minimizeToNotificationArea, closeToNotificationArea);
        }
        catch
        {
            return defaults;
        }
    }

    internal static void Save(UserSettings settings, string path = DefaultPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue("Preset", settings.Preset, RegistryValueKind.String);
        key.SetValue("Width", checked((int)settings.Width), RegistryValueKind.DWord);
        key.SetValue("Height", checked((int)settings.Height), RegistryValueKind.DWord);
        key.SetValue("RefreshHz", checked((int)settings.RefreshHz), RegistryValueKind.DWord);
        key.SetValue("MakePrimary", settings.MakePrimary ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("RouteNewWindows", settings.RouteNewWindows ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(
            "MinimizeToNotificationArea", settings.MinimizeToNotificationArea ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(
            "CloseToNotificationArea", settings.CloseToNotificationArea ? 1 : 0, RegistryValueKind.DWord);
    }

    private static uint ReadUInt(RegistryKey key, string name, uint fallback) =>
        key.GetValue(name) is int value && value >= 0 ? (uint)value : fallback;

    private static bool ReadBoolean(RegistryKey key, string name, bool fallback) =>
        key.GetValue(name) is int value ? value != 0 : fallback;
}
