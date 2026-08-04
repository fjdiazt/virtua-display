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
    private const uint GetTargetName = 2;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorNotFound = 1168;
    private const uint DevpropTypeGuid = 0x0000000D;
    private static readonly Guid MonitorInterfaceGuid = new("e6f07b5f-ee97-4a90-b076-33f57bf4eaa7");
    private static readonly DevPropKey DeviceContainerIdKey = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
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

    internal static bool MatchesRecoveryOwner(Guid expectedContainerId, Guid actualContainerId) =>
        expectedContainerId == actualContainerId;

    internal static bool TryGetOwnedDisplayName(
        Guid containerId,
        out string deviceName)
    {
        deviceName = string.Empty;

        foreach (var path in GetActiveDisplayPaths())
        {
            if (!TryGetTargetMonitorPath(path.TargetInfo, out var monitorPath) ||
                !TryGetMonitorContainerId(monitorPath, out var actualContainerId) ||
                !MatchesRecoveryOwner(containerId, actualContainerId) ||
                !TryGetSourceName(path.SourceInfo, out deviceName))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    internal static DisplaySnapshot CreateFallbackRecoverySnapshot(
        DisplaySnapshot active,
        string virtualDeviceName)
    {
        var virtualDisplay = active.Displays.Single(display =>
            string.Equals(display.DeviceName, virtualDeviceName, StringComparison.OrdinalIgnoreCase));
        var physicalDisplays = active.Displays.Where(display =>
            !string.Equals(display.DeviceName, virtualDeviceName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (physicalDisplays.Length == 0)
            throw new InvalidOperationException("No physical display is available for recovery.");

        var primary = physicalDisplays.SingleOrDefault(display => display.Primary) ??
                      physicalDisplays.MinBy(display => RecoveryDistance(display, virtualDisplay))!;
        var origin = primary.Position;
        return new DisplaySnapshot(physicalDisplays.Select(display => display with
        {
            Position = new Point(display.Position.X - origin.X, display.Position.Y - origin.Y),
            Primary = string.Equals(display.DeviceName, primary.DeviceName, StringComparison.OrdinalIgnoreCase)
        }).ToArray());
    }

    private static long RecoveryDistance(DisplayState display, DisplayState virtualDisplay)
    {
        var displayCenterX = display.Position.X * 2L + display.Mode.Width;
        var virtualCenterX = virtualDisplay.Position.X * 2L + virtualDisplay.Mode.Width;
        var expectedTop = virtualDisplay.Position.Y + virtualDisplay.Mode.Height;
        return Math.Abs(displayCenterX - virtualCenterX) +
               2L * Math.Abs(display.Position.Y - expectedTop);
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

    private static bool TryGetDisplayName(AddedDisplay addedDisplay, out string deviceName)
    {
        deviceName = string.Empty;
        try
        {
            return TryGetDisplayPath(addedDisplay, out var path) &&
                   TryGetSourceName(path.SourceInfo, out deviceName);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDisplayPath(
        AddedDisplay display,
        out DisplayConfigPathInfo matchingPath)
    {
        var targetLuid = Luid.FromInt64(display.AdapterLuid);
        foreach (var path in GetActiveDisplayPaths())
        {
            if (path.TargetInfo.AdapterId.Equals(targetLuid) &&
                path.TargetInfo.Id == display.TargetId)
            {
                matchingPath = path;
                return true;
            }
        }

        matchingPath = default;
        return false;
    }

    private static DisplayConfigPathInfo[] GetActiveDisplayPaths()
    {
        var result = GetDisplayConfigBufferSizes(
            QdcOnlyActivePaths, out var pathCount, out var modeCount);
        if (result != 0)
            throw new Win32Exception(result, "Could not size active display paths.");

        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        result = QueryDisplayConfig(
            QdcOnlyActivePaths,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero);
        if (result != 0)
            throw new Win32Exception(result, "Could not query active display paths.");

        return paths.Take(checked((int)pathCount)).ToArray();
    }

    private static unsafe bool TryGetSourceName(
        DisplayConfigPathSourceInfo source,
        out string deviceName)
    {
        var request = new DisplayConfigSourceDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = GetSourceName,
                Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                AdapterId = source.AdapterId,
                Id = source.Id
            }
        };
        var result = DisplayConfigGetDeviceInfo(ref request);
        if (result != 0)
            throw new Win32Exception(result, "Could not read display source name.");

        deviceName = new string(request.ViewGdiDeviceName);
        return !string.IsNullOrWhiteSpace(deviceName);
    }

    private static unsafe bool TryGetTargetMonitorPath(
        DisplayConfigPathTargetInfo target,
        out string monitorPath)
    {
        var request = new DisplayConfigTargetDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = GetTargetName,
                Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                AdapterId = target.AdapterId,
                Id = target.Id
            }
        };
        var result = DisplayConfigGetDeviceInfo(ref request);
        if (result != 0)
            throw new Win32Exception(result, "Could not read display target name.");

        monitorPath = new string(request.MonitorDevicePath);
        return !string.IsNullOrWhiteSpace(monitorPath);
    }

    private static bool TryGetMonitorContainerId(string monitorPath, out Guid containerId)
    {
        containerId = default;
        var interfaceGuid = MonitorInterfaceGuid;
        var infoSet = SetupDiGetClassDevsW(
            ref interfaceGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (infoSet == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not enumerate monitor interfaces.");

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new DeviceInterfaceData
                {
                    Size = (uint)Marshal.SizeOf<DeviceInterfaceData>()
                };
                if (!SetupDiEnumDeviceInterfaces(
                        infoSet, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                        return false;
                    throw new Win32Exception(error, "Could not enumerate a monitor interface.");
                }

                var deviceInfo = new DeviceInfoData
                {
                    Size = (uint)Marshal.SizeOf<DeviceInfoData>()
                };
                SetupDiGetDeviceInterfaceDetailW(
                    infoSet,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out var requiredSize,
                    ref deviceInfo);
                var detailError = Marshal.GetLastWin32Error();
                if (requiredSize == 0 || detailError != ErrorInsufficientBuffer)
                    throw new Win32Exception(detailError, "Could not size a monitor interface path.");

                var detail = Marshal.AllocHGlobal(checked((int)requiredSize));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    deviceInfo.Size = (uint)Marshal.SizeOf<DeviceInfoData>();
                    if (!SetupDiGetDeviceInterfaceDetailW(
                            infoSet,
                            ref interfaceData,
                            detail,
                            requiredSize,
                            out _,
                            ref deviceInfo))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(),
                            "Could not read a monitor interface path.");
                    }

                    var candidatePath = Marshal.PtrToStringUni(detail + 4);
                    if (!string.Equals(candidatePath, monitorPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var propertyKey = DeviceContainerIdKey;
                    var value = new byte[16];
                    if (!SetupDiGetDevicePropertyW(
                            infoSet,
                            ref deviceInfo,
                            ref propertyKey,
                            out var propertyType,
                            value,
                            (uint)value.Length,
                            out _,
                            0))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error == ErrorNotFound)
                            return false;
                        throw new Win32Exception(error, "Could not read monitor container ID.");
                    }

                    if (propertyType != DevpropTypeGuid)
                        return false;
                    containerId = new Guid(value);
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }
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

        internal long ToInt64() => ((long)HighPart << 32) | LowPart;

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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct DisplayConfigTargetDeviceName
    {
        internal DisplayConfigDeviceInfoHeader Header;
        internal uint Flags;
        internal uint OutputTechnology;
        internal ushort EdidManufactureId;
        internal ushort EdidProductCodeId;
        internal uint ConnectorInstance;
        internal fixed char MonitorFriendlyDeviceName[64];
        internal fixed char MonitorDevicePath[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        internal uint Size;
        internal Guid InterfaceClassGuid;
        internal uint Flags;
        internal UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        internal uint Size;
        internal Guid ClassGuid;
        internal uint DeviceInstance;
        internal UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        internal Guid FormatId;
        internal uint PropertyId;

        internal DevPropKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
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

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid,
        string? enumerator,
        IntPtr parent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref DeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet,
        ref DeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        ref DeviceInfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDevicePropertyW(
        IntPtr deviceInfoSet,
        ref DeviceInfoData deviceInfoData,
        ref DevPropKey propertyKey,
        out uint propertyType,
        [Out] byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
