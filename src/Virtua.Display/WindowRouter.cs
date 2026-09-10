using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace Virtua.Display;

internal readonly record struct WindowCandidate(
    bool Visible,
    bool Cloaked,
    bool ToolWindow,
    bool NoActivate,
    uint ProcessId);

internal sealed class WindowRouter : IDisposable, IAsyncDisposable
{
    private const uint EventObjectShow = 0x8002;
    private const int ObjectIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint WineventOutOfContext = 0;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const uint GaRoot = 2;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private const uint DwmwaCloaked = 14;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpAsyncWindowPos = 0x4000;
    private const uint MonitorDefaultToNull = 0;

    private readonly Rectangle _targetBounds;
    private readonly uint _ownProcessId;
    private readonly uint _shellProcessId;
    private readonly Action<string>? _reportError;
    private readonly Channel<IntPtr> _queue;
    private readonly ConcurrentDictionary<IntPtr, byte> _seen = new();
    private readonly WinEventDelegate _callback;
    private readonly Task _worker;
    private IntPtr _hook;
    private int _disposed;

    private WindowRouter(Rectangle targetBounds, Action<string>? reportError)
    {
        _targetBounds = targetBounds;
        _reportError = reportError;
        _ownProcessId = checked((uint)Environment.ProcessId);
        _shellProcessId = GetProcessId(GetShellWindow());
        _queue = Channel.CreateUnbounded<IntPtr>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _callback = OnWindowShown;
        _worker = Task.Run(RouteQueuedWindowsAsync);

        _hook = SetWinEventHook(
            EventObjectShow,
            EventObjectShow,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);

        if (_hook == IntPtr.Zero)
        {
            _queue.Writer.TryComplete();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install new-window hook.");
        }
    }

    internal static WindowRouter Start(Rectangle targetBounds, Action<string>? reportError = null)
    {
        if (targetBounds.Width <= 0 || targetBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetBounds));

        return new WindowRouter(targetBounds, reportError);
    }

    internal static bool IsEligible(WindowCandidate candidate, uint ownProcessId, uint shellProcessId) =>
        candidate.Visible &&
        !candidate.Cloaked &&
        !candidate.ToolWindow &&
        !candidate.NoActivate &&
        candidate.ProcessId != 0 &&
        candidate.ProcessId != ownProcessId &&
        candidate.ProcessId != shellProcessId;

    internal static bool ShouldRelocate(
        WindowCandidate candidate,
        bool isRoot,
        bool isShellWindow,
        bool onSourceMonitor) =>
        isRoot &&
        !isShellWindow &&
        onSourceMonitor &&
        candidate.Visible &&
        !candidate.Cloaked &&
        !candidate.ToolWindow &&
        !candidate.NoActivate &&
        candidate.ProcessId != 0;

    internal static Rectangle CalculateRelocatedBounds(
        Rectangle windowBounds,
        Rectangle sourceBounds,
        Rectangle destinationBounds)
    {
        if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceBounds));
        if (destinationBounds.Width <= 0 || destinationBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(destinationBounds));

        var width = Math.Clamp(windowBounds.Width, 1, destinationBounds.Width);
        var height = Math.Clamp(windowBounds.Height, 1, destinationBounds.Height);
        var x = destinationBounds.Left + windowBounds.Left - sourceBounds.Left;
        var y = destinationBounds.Top + windowBounds.Top - sourceBounds.Top;

        return new Rectangle(
            Math.Clamp(x, destinationBounds.Left, destinationBounds.Right - width),
            Math.Clamp(y, destinationBounds.Top, destinationBounds.Bottom - height),
            width,
            height);
    }

    internal static void RelocateWindows(
        Rectangle sourceBounds,
        Rectangle destinationBounds,
        Action<string>? reportError = null)
    {
        if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceBounds));
        if (destinationBounds.Width <= 0 || destinationBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(destinationBounds));

        var sourceRect = new NativeRect
        {
            Left = sourceBounds.Left,
            Top = sourceBounds.Top,
            Right = sourceBounds.Right,
            Bottom = sourceBounds.Bottom
        };
        var sourceMonitor = MonitorFromRect(ref sourceRect, MonitorDefaultToNull);
        if (sourceMonitor == IntPtr.Zero)
            throw new InvalidOperationException("Virtual display monitor could not be resolved.");

        var shellWindow = GetShellWindow();
        EnumWindowsDelegate callback = (window, _) =>
        {
            try
            {
                var candidate = ReadCandidate(window);
                if (!ShouldRelocate(
                        candidate,
                        GetAncestor(window, GaRoot) == window,
                        window == shellWindow,
                        MonitorFromWindow(window, MonitorDefaultToNull) == sourceMonitor) ||
                    !GetWindowRect(window, out var current))
                {
                    return true;
                }

                var target = CalculateRelocatedBounds(
                    new Rectangle(
                        current.Left,
                        current.Top,
                        current.Right - current.Left,
                        current.Bottom - current.Top),
                    sourceBounds,
                    destinationBounds);

                if (!SetWindowPos(
                        window,
                        IntPtr.Zero,
                        target.Left,
                        target.Top,
                        target.Width,
                        target.Height,
                        SwpNoZOrder | SwpNoActivate))
                {
                    reportError?.Invoke(
                        $"Could not relocate window 0x{window.ToInt64():X}: " +
                        new Win32Exception(Marshal.GetLastWin32Error()).Message);
                }
            }
            catch (Exception exception)
            {
                AppLog.Error($"Could not relocate window 0x{window.ToInt64():X}.", exception);
                reportError?.Invoke(
                    $"Could not relocate window 0x{window.ToInt64():X}: {exception.Message}");
            }

            return true;
        };

        if (!EnumWindows(callback, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate desktop windows.");

        GC.KeepAlive(callback);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
        if (hook != IntPtr.Zero)
            UnhookWinEvent(hook);

        _queue.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
        _seen.Clear();
        GC.KeepAlive(_callback);
    }

    private void OnWindowShown(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            window == IntPtr.Zero ||
            objectId != ObjectIdWindow ||
            childId != ChildIdSelf ||
            GetAncestor(window, GaRoot) != window)
        {
            return;
        }

        try
        {
            var candidate = ReadCandidate(window);
            if (IsEligible(candidate, _ownProcessId, _shellProcessId) && _seen.TryAdd(window, 0))
                _queue.Writer.TryWrite(window);
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not inspect window 0x{window.ToInt64():X}.", exception);
            _reportError?.Invoke($"Could not inspect window 0x{window.ToInt64():X}: {exception.Message}");
        }
    }

    private async Task RouteQueuedWindowsAsync()
    {
        await foreach (var window in _queue.Reader.ReadAllAsync())
        {
            try
            {
                Route(window);
            }
            catch (Exception exception)
            {
                AppLog.Error($"Could not route window 0x{window.ToInt64():X}.", exception);
                _reportError?.Invoke($"Could not route window 0x{window.ToInt64():X}: {exception.Message}");
            }
        }
    }

    private void Route(IntPtr window)
    {
        if (!IsWindow(window) || !GetWindowRect(window, out var current))
            return;

        var width = Math.Clamp(current.Right - current.Left, 1, _targetBounds.Width);
        var height = Math.Clamp(current.Bottom - current.Top, 1, _targetBounds.Height);
        var x = _targetBounds.Left + Math.Max(0, (_targetBounds.Width - width) / 2);
        var y = _targetBounds.Top + Math.Max(0, (_targetBounds.Height - height) / 2);

        if (!SetWindowPos(
                window,
                IntPtr.Zero,
                x,
                y,
                width,
                height,
                SwpNoZOrder | SwpNoActivate | SwpAsyncWindowPos))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static WindowCandidate ReadCandidate(IntPtr window)
    {
        var styles = GetWindowLongPtrW(window, GwlExStyle).ToInt64();
        var cloaked = 0;
        _ = DwmGetWindowAttribute(window, DwmwaCloaked, out cloaked, sizeof(int));

        return new WindowCandidate(
            IsWindowVisible(window),
            cloaked != 0,
            (styles & WsExToolWindow) != 0,
            (styles & WsExNoActivate) != 0,
            GetProcessId(window));
    }

    private static uint GetProcessId(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return 0;

        GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    private delegate bool EnumWindowsDelegate(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHookModule,
        WinEventDelegate eventHook,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr eventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rectangle, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr window,
        uint attribute,
        out int value,
        int valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
