using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Virtua.Display;

internal sealed class SudoVdaClient : IDisposable
{
    internal const uint IoctlAdd = 0x00222000;
    internal const uint IoctlRemove = 0x00222004;
    internal const uint IoctlGetWatchdog = 0x0022200C;
    internal const uint IoctlPing = 0x00222220;
    internal const uint IoctlGetProtocol = 0x002223FC;

    private static readonly Guid InterfaceGuid = new("e5bcc234-1e0c-418a-a0d4-ef8b7501414d");

    private readonly SafeFileHandle _handle;

    private SudoVdaClient(SafeFileHandle handle)
    {
        _handle = handle;
    }

    internal static SudoVdaClient Open()
    {
        var client = new SudoVdaClient(OpenDevice());

        try
        {
            var version = client.GetProtocolVersion();
            var status = DriverStatus.FromProtocol(
                version.Major, version.Minor, version.Incremental);
            if (status.Kind != DriverStatusKind.Ready)
                throw new InvalidOperationException(status.Message);

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal static DriverStatus Probe()
    {
        try
        {
            using var client = new SudoVdaClient(OpenDevice());
            var version = client.GetProtocolVersion();
            return DriverStatus.FromProtocol(
                version.Major, version.Minor, version.Incremental);
        }
        catch (DriverNotFoundException exception)
        {
            return new(DriverStatusKind.Missing, exception.Message);
        }
        catch (Exception exception)
        {
            AppLog.Error("SudoVDA probe failed.", exception);
            return new(DriverStatusKind.Error, exception.Message);
        }
    }

    internal WatchdogOut GetWatchdog()
    {
        if (!DeviceIoControlOut(
                _handle,
                IoctlGetWatchdog,
                IntPtr.Zero,
                0,
                out WatchdogOut output,
                (uint)Marshal.SizeOf<WatchdogOut>(),
                out _,
                IntPtr.Zero))
        {
            ThrowLastWin32("read SudoVDA watchdog");
        }

        return output;
    }

    internal unsafe AddedDisplay Add(DisplayMode mode, Guid monitorGuid)
    {
        var input = new AddParams
        {
            Width = mode.Width,
            Height = mode.Height,
            RefreshRate = mode.RefreshHz,
            MonitorGuid = monitorGuid
        };

        WriteAscii(input.DeviceName, 14, "VirtuaDisplay");
        WriteAscii(input.SerialNumber, 14, "VD0001");

        if (!DeviceIoControlAdd(
                _handle,
                IoctlAdd,
                ref input,
                (uint)Marshal.SizeOf<AddParams>(),
                out AddOut output,
                (uint)Marshal.SizeOf<AddOut>(),
                out _,
                IntPtr.Zero))
        {
            ThrowLastWin32("add SudoVDA monitor");
        }

        return new AddedDisplay(output.AdapterLuid, output.TargetId);
    }

    internal void Remove(Guid monitorGuid)
    {
        var input = new RemoveParams { MonitorGuid = monitorGuid };
        if (!DeviceIoControlRemove(
                _handle,
                IoctlRemove,
                ref input,
                (uint)Marshal.SizeOf<RemoveParams>(),
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero))
        {
            ThrowLastWin32("remove SudoVDA monitor");
        }
    }

    internal void Ping()
    {
        if (!DeviceIoControl(
                _handle,
                IoctlPing,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero))
        {
            ThrowLastWin32("ping SudoVDA driver");
        }
    }

    internal ProtocolVersion GetProtocolVersion()
    {
        if (!DeviceIoControlProtocol(
                _handle,
                IoctlGetProtocol,
                IntPtr.Zero,
                0,
                out ProtocolVersion output,
                (uint)Marshal.SizeOf<ProtocolVersion>(),
                out _,
                IntPtr.Zero))
        {
            ThrowLastWin32("read SudoVDA protocol version");
        }

        return output;
    }

    public void Dispose() => _handle.Dispose();

    private static SafeFileHandle OpenDevice()
    {
        int? openError = null;
        var interfaceGuid = InterfaceGuid;
        var deviceInfoSet = SetupDiGetClassDevsW(
            ref interfaceGuid,
            null,
            IntPtr.Zero,
            0x00000002 | 0x00000010);

        if (deviceInfoSet == new IntPtr(-1))
            ThrowLastWin32("enumerate SudoVDA interfaces");

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new DeviceInterfaceData
                {
                    Size = (uint)Marshal.SizeOf<DeviceInterfaceData>()
                };

                if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref interfaceGuid,
                        index,
                        ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259)
                        break;
                    throw new Win32Exception(error, "Could not enumerate SudoVDA interface.");
                }

                SetupDiGetDeviceInterfaceDetailW(
                    deviceInfoSet,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out var requiredSize,
                    IntPtr.Zero);

                var buffer = Marshal.AllocHGlobal(checked((int)requiredSize));
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(
                            deviceInfoSet,
                            ref interfaceData,
                            buffer,
                            requiredSize,
                            out _,
                            IntPtr.Zero))
                    {
                        continue;
                    }

                    var path = Marshal.PtrToStringUni(buffer + 4);
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    var handle = CreateFileW(
                        path,
                        0x80000000 | 0x40000000,
                        0x00000001 | 0x00000002,
                        IntPtr.Zero,
                        3,
                        0x00000080,
                        IntPtr.Zero);

                    if (!handle.IsInvalid)
                        return handle;

                    openError = Marshal.GetLastWin32Error();
                    handle.Dispose();
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        if (openError is int openDeviceError)
            throw new Win32Exception(openDeviceError,
                $"Could not open SudoVDA interface (Win32 {openDeviceError}).");

        throw new DriverNotFoundException();
    }

    private static unsafe void WriteAscii(byte* target, int capacity, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var count = Math.Min(bytes.Length, capacity - 1);
        for (var index = 0; index < count; index++)
            target[index] = bytes[index];
        target[count] = 0;
    }

    private static void ThrowLastWin32(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        throw new Win32Exception(error, $"Could not {operation} (Win32 {error}).");
    }

    private sealed class DriverNotFoundException : InvalidOperationException
    {
        internal DriverNotFoundException()
            : base("SudoVDA device interface not found. Install or repair the Virtua Display driver.")
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProtocolVersion
    {
        internal byte Major;
        internal byte Minor;
        internal byte Incremental;
        internal byte TestBuild;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal unsafe struct AddParams
    {
        internal uint Width;
        internal uint Height;
        internal uint RefreshRate;
        internal Guid MonitorGuid;
        internal fixed byte DeviceName[14];
        internal fixed byte SerialNumber[14];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AddOut
    {
        internal uint AdapterLowPart;
        internal int AdapterHighPart;
        internal uint TargetId;

        internal readonly long AdapterLuid => ((long)AdapterHighPart << 32) | AdapterLowPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RemoveParams
    {
        internal Guid MonitorGuid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WatchdogOut
    {
        internal uint Timeout;
        internal uint Countdown;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        internal uint Size;
        internal Guid InterfaceClassGuid;
        internal uint Flags;
        internal UIntPtr Reserved;
    }

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
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr input,
        uint inputSize,
        IntPtr output,
        uint outputSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlAdd(
        SafeFileHandle device,
        uint controlCode,
        ref AddParams input,
        uint inputSize,
        out AddOut output,
        uint outputSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlRemove(
        SafeFileHandle device,
        uint controlCode,
        ref RemoveParams input,
        uint inputSize,
        IntPtr output,
        uint outputSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlOut(
        SafeFileHandle device,
        uint controlCode,
        IntPtr input,
        uint inputSize,
        out WatchdogOut output,
        uint outputSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlProtocol(
        SafeFileHandle device,
        uint controlCode,
        IntPtr input,
        uint inputSize,
        out ProtocolVersion output,
        uint outputSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
