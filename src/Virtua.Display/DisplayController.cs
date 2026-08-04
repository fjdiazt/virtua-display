using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Virtua.Display;

internal static class DisplayController
{
    private const uint DisplayDeviceActive = 0x00000001;
    private const uint DisplayDevicePrimary = 0x00000004;
    private const uint DisplayDeviceMirroring = 0x00000008;
    private const uint DmPosition = 0x00000020;
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint DmDisplayFrequency = 0x00400000;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsNoReset = 0x10000000;
    private const uint CdsSetPrimary = 0x00000010;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint GetSourceName = 1;
    private const int EnumCurrentSettings = -1;

    internal static DisplayMode GetPrimaryMode() =>
        Capture().Displays.Single(display => display.Primary).Mode;

    internal static DisplaySnapshot Capture()
    {
        var displays = new List<DisplayState>();

        for (uint index = 0; ; index++)
        {
            var device = NewDisplayDevice();
            if (!EnumDisplayDevicesW(null, index, ref device, 0))
                break;

            if ((device.StateFlags & DisplayDeviceActive) == 0 ||
                (device.StateFlags & DisplayDeviceMirroring) != 0)
            {
                continue;
            }

            if (!TryGetSettings(device.DeviceName, EnumCurrentSettings, out var mode))
                continue;

            displays.Add(new DisplayState(
                device.DeviceName,
                new Point(mode.Position.X, mode.Position.Y),
                new DisplayMode(mode.PelsWidth, mode.PelsHeight, mode.DisplayFrequency),
                (device.StateFlags & DisplayDevicePrimary) != 0));
        }

        if (displays.Count == 0 || displays.Count(display => display.Primary) != 1)
            throw new InvalidOperationException("Windows did not report exactly one active primary display.");

        return new DisplaySnapshot(displays);
    }

    internal static IReadOnlyList<DisplayMode> GetModeChoices()
    {
        var primary = Capture().Displays.Single(display => display.Primary);
        var modes = new List<DisplayMode>();

        for (var index = 0; TryGetSettings(primary.DeviceName, index, out var mode); index++)
        {
            var candidate = new DisplayMode(mode.PelsWidth, mode.PelsHeight, mode.DisplayFrequency);
            if (IsSupported(candidate))
                modes.Add(candidate);
        }

        modes.AddRange(
        [
            new DisplayMode(1080, 1080, 60),
            new DisplayMode(1440, 1440, 60),
            new DisplayMode(2160, 2160, 60),
            new DisplayMode(1280, 720, 60),
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(1920, 1080, 120),
            new DisplayMode(2560, 1440, 60),
            new DisplayMode(2560, 1440, 120),
            new DisplayMode(3840, 2160, 60),
            new DisplayMode(3840, 2160, 120)
        ]);

        return DistinctModes(modes.Where(IsSupported));
    }

    internal static IReadOnlyList<DisplayMode> DistinctModes(IEnumerable<DisplayMode> modes) =>
        modes
            .Distinct()
            .OrderBy(mode => mode.Width)
            .ThenBy(mode => mode.Height)
            .ThenBy(mode => mode.RefreshHz)
            .ToArray();

    internal static bool IsSupported(DisplayMode mode) =>
        mode.Width is >= 640 and <= 7680 &&
        mode.Height is >= 480 and <= 4320 &&
        mode.RefreshHz is >= 1 and <= 500;

    internal static async Task<string> WaitForDisplayAsync(
        AddedDisplay addedDisplay,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetDisplayName(addedDisplay, out var deviceName))
                return deviceName;

            await Task.Delay(50, cancellationToken);
        } while (DateTime.UtcNow < deadline);

        throw new TimeoutException(
            $"SudoVDA target {addedDisplay.TargetId} did not become an active Windows display within {timeout.TotalSeconds:0.#} seconds.");
    }

    internal static Rectangle PlaceAndSetPrimary(
        string deviceName,
        DisplayMode mode,
        bool makePrimary)
    {
        if (!IsSupported(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), $"Unsupported display mode: {mode}.");

        var active = Capture();
        var current = active.Displays.Single(display =>
            string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        var position = ChoosePosition(active, deviceName);
        if (current.Position != position || current.Mode != mode)
        {
            ApplyState(new DisplayState(deviceName, position, mode, false), false, true);
            CommitChanges("place virtual display");
        }

        if (makePrimary)
            MakePrimary(deviceName);

        return GetBounds(deviceName);
    }

    internal static Point ChoosePosition(DisplaySnapshot snapshot, string deviceName)
    {
        var target = snapshot.Displays.Single(display =>
            string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        var primary = snapshot.Displays.Single(display => display.Primary);
        return new Point(
            primary.Position.X + (checked((int)primary.Mode.Width) - checked((int)target.Mode.Width)) / 2,
            primary.Position.Y - checked((int)target.Mode.Height));
    }

    internal static void Restore(DisplaySnapshot snapshot)
    {
        foreach (var display in snapshot.Displays)
            ApplyState(display, display.Primary, false);

        CommitChanges("restore display topology");
    }

    internal static Rectangle GetBounds(string deviceName)
    {
        if (!TryGetSettings(deviceName, EnumCurrentSettings, out var mode))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not read display settings for {deviceName}.");

        return new Rectangle(
            mode.Position.X,
            mode.Position.Y,
            checked((int)mode.PelsWidth),
            checked((int)mode.PelsHeight));
    }

    internal static void MakePrimary(string deviceName)
    {
        var displays = Capture().Displays;
        var target = displays.SingleOrDefault(display =>
            string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

        if (target is null)
            throw new InvalidOperationException($"Display {deviceName} is not active.");

        foreach (var display in displays.OrderByDescending(display =>
                     string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)))
        {
            var shifted = display with
            {
                Position = new Point(
                    display.Position.X - target.Position.X,
                    display.Position.Y - target.Position.Y)
            };
            ApplyState(shifted, string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase), false);
        }

        CommitChanges($"make {deviceName} primary");
    }

    private static void ApplyState(DisplayState state, bool setPrimary, bool applyMode)
    {
        if (!TryGetSettings(state.DeviceName, EnumCurrentSettings, out var mode))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Could not read current display settings for {state.DeviceName}.");
        mode.Position = new PointL { X = state.Position.X, Y = state.Position.Y };
        mode.Fields = DmPosition;
        if (applyMode)
        {
            mode.PelsWidth = state.Mode.Width;
            mode.PelsHeight = state.Mode.Height;
            mode.DisplayFrequency = state.Mode.RefreshHz;
            mode.Fields |= DmPelsWidth | DmPelsHeight | DmDisplayFrequency;
        }

        var flags = CdsUpdateRegistry | CdsNoReset;
        if (setPrimary)
            flags |= CdsSetPrimary;

        var result = ChangeDisplaySettingsExW(state.DeviceName, ref mode, IntPtr.Zero, flags, IntPtr.Zero);
        if (result != 0)
            throw new InvalidOperationException($"Display change for {state.DeviceName} failed with DISP_CHANGE {result}.");
    }

    private static void CommitChanges(string operation)
    {
        var result = ChangeDisplaySettingsExW(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (result != 0)
            throw new InvalidOperationException($"Could not {operation}; DISP_CHANGE {result}.");
    }

    private static bool TryGetSettings(string deviceName, int modeIndex, out DevMode mode)
    {
        mode = NewDevMode();
        return EnumDisplaySettingsW(deviceName, modeIndex, ref mode);
    }

    private static unsafe bool TryGetDisplayName(AddedDisplay addedDisplay, out string deviceName)
    {
        deviceName = string.Empty;
        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0)
            return false;

        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        if (QueryDisplayConfig(
                QdcOnlyActivePaths,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero) != 0)
        {
            return false;
        }

        var targetLuid = Luid.FromInt64(addedDisplay.AdapterLuid);
        foreach (var path in paths.Take(checked((int)pathCount)))
        {
            if (!path.TargetInfo.AdapterId.Equals(targetLuid) ||
                path.TargetInfo.Id != addedDisplay.TargetId)
            {
                continue;
            }

            var sourceName = new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = GetSourceName,
                    Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                    AdapterId = path.SourceInfo.AdapterId,
                    Id = path.SourceInfo.Id
                }
            };

            if (DisplayConfigGetDeviceInfo(ref sourceName) != 0)
                return false;

            deviceName = new string(sourceName.ViewGdiDeviceName);
            return !string.IsNullOrWhiteSpace(deviceName);
        }

        return false;
    }

    private static DisplayDevice NewDisplayDevice() => new()
    {
        Size = (uint)Marshal.SizeOf<DisplayDevice>()
    };

    private static DevMode NewDevMode() => new()
    {
        Size = checked((ushort)Marshal.SizeOf<DevMode>())
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        internal uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceString;
        internal uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
        internal ushort SpecVersion;
        internal ushort DriverVersion;
        internal ushort Size;
        internal ushort DriverExtra;
        internal uint Fields;
        internal PointL Position;
        internal uint DisplayOrientation;
        internal uint DisplayFixedOutput;
        internal short Color;
        internal short Duplex;
        internal short YResolution;
        internal short TTOption;
        internal short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string FormName;
        internal ushort LogPixels;
        internal uint BitsPerPel;
        internal uint PelsWidth;
        internal uint PelsHeight;
        internal uint DisplayFlags;
        internal uint DisplayFrequency;
        internal uint ICMMethod;
        internal uint ICMIntent;
        internal uint MediaType;
        internal uint DitherType;
        internal uint Reserved1;
        internal uint Reserved2;
        internal uint PanningWidth;
        internal uint PanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Luid : IEquatable<Luid>
    {
        internal readonly uint LowPart;
        internal readonly int HighPart;

        internal static Luid FromInt64(long value) => new((uint)value, (int)(value >> 32));

        private Luid(uint lowPart, int highPart)
        {
            LowPart = lowPart;
            HighPart = highPart;
        }

        public bool Equals(Luid other) => LowPart == other.LowPart && HighPart == other.HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        internal Luid AdapterId;
        internal uint Id;
        internal uint ModeInfoIdx;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational
    {
        internal uint Numerator;
        internal uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        internal Luid AdapterId;
        internal uint Id;
        internal uint ModeInfoIdx;
        internal uint OutputTechnology;
        internal uint Rotation;
        internal uint Scaling;
        internal Rational RefreshRate;
        internal uint ScanLineOrdering;
        internal int TargetAvailable;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        internal DisplayConfigPathSourceInfo SourceInfo;
        internal DisplayConfigPathTargetInfo TargetInfo;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct DisplayConfigModeUnion
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        internal uint InfoType;
        internal uint Id;
        internal Luid AdapterId;
        internal DisplayConfigModeUnion ModeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        internal uint Type;
        internal uint Size;
        internal Luid AdapterId;
        internal uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct DisplayConfigSourceDeviceName
    {
        internal DisplayConfigDeviceInfoHeader Header;
        internal fixed char ViewGdiDeviceName[32];
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevicesW(
        string? device,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(
        string deviceName,
        int modeNum,
        ref DevMode devMode);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsExW(
        string? deviceName,
        ref DevMode devMode,
        IntPtr window,
        uint flags,
        IntPtr parameters);

    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsExW(
        string? deviceName,
        IntPtr devMode,
        IntPtr window,
        uint flags,
        IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint pathCount,
        out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);
}
