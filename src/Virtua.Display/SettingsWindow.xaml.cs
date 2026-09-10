using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace Virtua.Display;

public sealed partial class SettingsWindow : System.Windows.Window
{
    private readonly MainWindow _settingsOwner;

    internal SettingsWindow(MainWindow settingsOwner)
    {
        _settingsOwner = settingsOwner;
        InitializeComponent();
        ApplySettings();
        SourceInitialized += (_, _) => ApplyDarkCaption();
    }

    private void StartWithWindows_Click(object sender, System.Windows.RoutedEventArgs eventArgs) =>
        RunAction(((ToggleButton)sender).IsChecked == true, _settingsOwner.SetStartWithWindows);

    private void MinimizeToNotificationArea_Click(
        object sender,
        System.Windows.RoutedEventArgs eventArgs) =>
        RunAction(
            ((ToggleButton)sender).IsChecked == true,
            _settingsOwner.SetMinimizeToNotificationArea);

    private void KeepRunningWhenClosed_Click(
        object sender,
        System.Windows.RoutedEventArgs eventArgs) =>
        RunAction(
            ((ToggleButton)sender).IsChecked == true,
            _settingsOwner.SetCloseToNotificationArea);

    private void RunAction(bool enabled, Action<bool> action)
    {
        try
        {
            action(enabled);
            ApplySettings();
            SetStatus("Saved", "ActiveBrush");
        }
        catch (Exception exception)
        {
            AppLog.Error("Settings action failed.", exception);
            ApplySettings();
            SetStatus(exception.Message, "ErrorBrush");
        }
    }

    private void ApplySettings()
    {
        _startWithWindowsSwitch.IsChecked = _settingsOwner.StartWithWindowsEnabled;
        _minimizeToNotificationAreaSwitch.IsChecked =
            _settingsOwner.MinimizeToNotificationAreaEnabled;
        _keepRunningWhenClosedSwitch.IsChecked =
            _settingsOwner.CloseToNotificationAreaEnabled;
    }

    private void SetStatus(string message, string brushKey)
    {
        settingsStatusText.Text = message;
        settingsStatusText.Foreground = (Brush)FindResource(brushKey);
    }

    private void ApplyDarkCaption()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        var captionColor = 0x00201511;
        var textColor = 0x00FCF7F5;
        DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        DwmSetWindowAttribute(handle, 35, ref captionColor, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref textColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int value,
        int valueSize);
}
