using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Virtua.Display;

internal sealed class NotificationAreaIcon : IDisposable
{
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NifMessage = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint NotifyIconVersion4 = 4;
    private const int CallbackMessage = 0x8001;
    private const int WmContextMenu = 0x007B;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDoubleClick = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int NinSelect = 0x0400;
    private const int NinKeySelect = 0x0401;

    private readonly IntPtr _windowHandle;
    private readonly HwndSource _source;
    private readonly Icon _icon;
    private readonly Action _open;
    private readonly ContextMenu _menu;
    private readonly uint _taskbarCreatedMessage;
    private NotifyIconData _data;
    private bool _visible;
    private bool _disposed;

    internal NotificationAreaIcon(
        Window window,
        Action open,
        Func<(string Label, bool Enabled)> getStartStopAction,
        Action startStop,
        Action exit)
    {
        _windowHandle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_windowHandle) ??
            throw new InvalidOperationException("Could not access the WPF window handle.");
        _icon = LoadApplicationIcon();
        _open = open;
        _menu = CreateMenu(open, getStartStopAction, startStop, exit);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _data = new NotifyIconData
        {
            Size = checked((uint)Marshal.SizeOf<NotifyIconData>()),
            WindowHandle = _windowHandle,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = CallbackMessage,
            IconHandle = _icon.Handle,
            Tip = "Virtua Display",
            Info = string.Empty,
            InfoTitle = string.Empty
        };
        _source.AddHook(WindowProcedure);
    }

    internal void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_visible)
            return;

        Add();
        _visible = true;
    }

    internal void Hide()
    {
        if (!_visible)
            return;

        ShellNotifyIcon(NimDelete, ref _data);
        _visible = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Hide();
        _source.RemoveHook(WindowProcedure);
        _icon.Dispose();
        _disposed = true;
    }

    private void Add()
    {
        if (!ShellNotifyIcon(NimAdd, ref _data))
            throw new InvalidOperationException("Could not add Virtua Display to the notification area.");

        _data.Version = NotifyIconVersion4;
        if (ShellNotifyIcon(NimSetVersion, ref _data))
            return;

        ShellNotifyIcon(NimDelete, ref _data);
        throw new InvalidOperationException("Could not initialize the Virtua Display notification icon.");
    }

    private IntPtr WindowProcedure(
        IntPtr window,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage && _visible)
        {
            try
            {
                Add();
            }
            catch
            {
                _visible = false;
                _open();
            }
            handled = true;
            return IntPtr.Zero;
        }

        if (message != CallbackMessage)
            return IntPtr.Zero;

        var notification = unchecked((int)(longParameter.ToInt64() & 0xFFFF));
        switch (notification)
        {
            case NinSelect:
            case NinKeySelect:
            case WmLButtonUp:
            case WmLButtonDoubleClick:
                _open();
                handled = true;
                break;
            case WmContextMenu:
            case WmRButtonUp:
                ShowMenu();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        if (_menu.IsOpen)
            return;

        _menu.IsOpen = true;
    }

    internal static ContextMenu CreateMenu(
        Action open,
        Func<(string Label, bool Enabled)> getStartStopAction,
        Action startStop,
        Action exit)
    {
        var state = getStartStopAction();
        var openItem = new MenuItem
        {
            Header = "Open Virtua Display",
            FontWeight = FontWeights.SemiBold
        };
        var startStopItem = new MenuItem
        {
            Header = state.Label,
            IsEnabled = state.Enabled
        };
        var exitItem = new MenuItem { Header = "Exit" };
        openItem.Click += (_, _) => open();
        startStopItem.Click += (_, _) => startStop();
        exitItem.Click += (_, _) => exit();

        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        menu.Items.Add(openItem);
        menu.Items.Add(startStopItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        menu.Opened += (_, _) =>
        {
            var current = getStartStopAction();
            startStopItem.Header = current.Label;
            startStopItem.IsEnabled = current.Enabled;
        };
        return menu;
    }

    private static Icon LoadApplicationIcon()
    {
        if (Environment.ProcessPath is { } path &&
            Icon.ExtractAssociatedIcon(path) is { } icon)
        {
            return icon;
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal uint Size;
        internal IntPtr WindowHandle;
        internal uint Id;
        internal uint Flags;
        internal uint CallbackMessage;
        internal IntPtr IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Tip;
        internal uint State;
        internal uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string Info;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid ItemGuid;
        internal IntPtr BalloonIconHandle;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
}
