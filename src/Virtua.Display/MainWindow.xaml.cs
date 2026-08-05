using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Virtua.Display;

public sealed partial class MainWindow : Window
{
    private static readonly Guid MonitorGuid = new("8d6a8a70-67e9-4af0-9e57-0fcb401ca31b");

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly DisplayMode _primaryMode;
    private readonly Action<UserSettings> _saveSettings;
    private readonly Action<bool> _setStartWithWindows;
    private readonly DriverStatus _driverStatus;

    private IReadOnlyList<ResolutionSize> _availableSizes = [];
    private ResolutionSize? _lockedAspectRatio;
    private UserSettings _lastValidSettings;
    private bool _suppressResolutionEvents;
    private bool _modeValid;
    private bool _busy;
    private bool _startWithWindowsEnabled;
    private NotificationAreaIcon? _notificationAreaIcon;

    private MonitorSession? _session;
    private bool _transitioning;
    private bool _closing;
    private bool _allowClose;
    private bool _exitRequested;
    private bool _recoveryAttempted;

    internal event Action? SettingsRequested;

    internal MainWindow() : this(null, null, null, null, null, null, null)
    {
        Dispatcher.BeginInvoke(
            new Action(async () => await RecoverSessionAsync()),
            DispatcherPriority.Loaded);
    }

    internal MainWindow(
        DisplayMode? primaryMode,
        UserSettings? settings,
        IReadOnlyList<DisplayMode>? availableModes,
        Action<UserSettings>? saveSettings,
        bool? startWithWindows = null,
        Action<bool>? setStartWithWindows = null,
        DriverStatus? driverStatus = null)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDarkCaption();
        _driverStatus = driverStatus ?? SudoVdaClient.Probe();
        _primaryMode = primaryMode ?? DisplayController.GetPrimaryMode();
        _lastValidSettings = settings ?? UserSettingsStore.Load(_primaryMode);
        _saveSettings = saveSettings ?? (value => UserSettingsStore.Save(value));
        _setStartWithWindows = setStartWithWindows ??
            (value => StartupRegistration.SetEnabled(value));
        _startWithWindowsEnabled = startWithWindows ?? StartupRegistration.IsEnabled();

        LoadResolutionControls(availableModes ?? DisplayController.GetModeChoices(), _lastValidSettings);
        _aspectCombo.SelectionChanged += (_, _) => OnAspectChanged();
        _presetCombo.SelectionChanged += (_, _) => OnPresetChanged();
        _widthText.TextChanged += (_, _) => OnDimensionChanged(true);
        _heightText.TextChanged += (_, _) => OnDimensionChanged(false);
        _aspectLockButton.Checked += (_, _) => OnAspectLockChanged();
        _aspectLockButton.Unchecked += (_, _) => OnAspectLockChanged();
        _refreshCombo.SelectionChanged += (_, _) => ValidateResolution();
        _primaryCheck.Checked += (_, _) => UpdatePersistedChecks();
        _primaryCheck.Unchecked += (_, _) => UpdatePersistedChecks();
        _routingCheck.Checked += (_, _) => UpdatePersistedChecks();
        _routingCheck.Unchecked += (_, _) => UpdatePersistedChecks();
        _settingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        _startStopButton.Click += async (_, _) => await ToggleAsync();
        StateChanged += (_, _) => OnWindowStateChanged();
        Closing += OnWindowClosing;
        Closed += (_, _) => _notificationAreaIcon?.Dispose();
        UpdateAspectLockButton();
        ValidateResolution(false);
        if (_driverStatus.Kind != DriverStatusKind.Ready)
            SetStatusError(_driverStatus.Message);
    }

    private void LoadResolutionControls(IReadOnlyList<DisplayMode> modes, UserSettings settings)
    {
        _suppressResolutionEvents = true;
        _availableSizes = ResolutionOptions.DistinctSizes(modes);
        _aspectCombo.Items.Add(new AspectFilterChoice("All aspect ratios", null));
        foreach (var ratio in ResolutionOptions.AspectRatios(_availableSizes))
            _aspectCombo.Items.Add(new AspectFilterChoice(ratio.FilterLabel, ratio));
        _aspectCombo.SelectedIndex = 0;
        RebuildPresetChoices(settings.Preset, false);

        foreach (var rate in ResolutionOptions.RefreshRates(_primaryMode.RefreshHz))
            _refreshCombo.Items.Add(rate);

        var choice = _presetCombo.Items
            .Cast<PresetChoice>()
            .SingleOrDefault(item => item.Key == settings.Preset) ??
            _presetCombo.Items.Cast<PresetChoice>().Single(item => item.Key == UserSettings.Custom);
        _presetCombo.SelectedItem = choice;
        var initialMode = choice.Key == UserSettings.CopyPrimary
            ? _primaryMode
            : new DisplayMode(settings.Width, settings.Height, settings.RefreshHz);
        SetModeText(initialMode, true);
        _primaryCheck.IsChecked = settings.MakePrimary;
        _routingCheck.IsChecked = settings.RouteNewWindows;
        _suppressResolutionEvents = false;
    }

    private void OnAspectChanged()
    {
        if (_suppressResolutionEvents)
            return;

        var preferredKey = _presetCombo.SelectedItem is PresetChoice { Size: not null } choice
            ? choice.Key
            : null;
        RebuildPresetChoices(preferredKey, true);
        OnPresetChanged();
    }

    private void RebuildPresetChoices(string? preferredKey, bool selectFirstNormal)
    {
        var suppressed = _suppressResolutionEvents;
        _suppressResolutionEvents = true;
        var ratio = (_aspectCombo.SelectedItem as AspectFilterChoice)?.Ratio;
        var sizes = ratio is null
            ? _availableSizes
            : _availableSizes.Where(size => ResolutionOptions.AspectRatio(size) == ratio).ToArray();

        _presetCombo.Items.Clear();
        _presetCombo.Items.Add(new PresetChoice(
            "Match primary display", UserSettings.CopyPrimary, null));
        foreach (var size in sizes)
        {
            var aspect = ResolutionOptions.AspectRatio(size);
            var label = ratio is null
                ? $"{size} ({aspect.ResolutionLabel})"
                : size.ToString();
            _presetCombo.Items.Add(new PresetChoice(label, size.Key, size));
        }
        _presetCombo.Items.Add(new PresetChoice("Custom", UserSettings.Custom, null));

        var choice = _presetCombo.Items.Cast<PresetChoice>()
            .SingleOrDefault(item => item.Key == preferredKey);
        if (selectFirstNormal && choice?.Size is null)
            choice = _presetCombo.Items.Cast<PresetChoice>().FirstOrDefault(item => item.Size is not null);
        choice ??= _presetCombo.Items.Cast<PresetChoice>()
            .FirstOrDefault(item => item.Size is not null);
        choice ??= _presetCombo.Items.Cast<PresetChoice>()
            .Single(item => item.Key == UserSettings.CopyPrimary);
        _presetCombo.SelectedItem = choice;
        _suppressResolutionEvents = suppressed;
    }

    private void OnPresetChanged()
    {
        if (_suppressResolutionEvents || _presetCombo.SelectedItem is not PresetChoice choice)
            return;

        if (choice.Key == UserSettings.CopyPrimary)
            SetModeText(_primaryMode, true);
        else if (choice.Size is { } size)
            SetModeText(new DisplayMode(size.Width, size.Height, SelectedRefresh()), false);
        ValidateResolution();
    }

    private void OnDimensionChanged(bool widthChanged)
    {
        if (_suppressResolutionEvents)
            return;

        if (_aspectLockButton.IsChecked == true &&
            _lockedAspectRatio is { } ratio &&
            TryScaleLockedDimension(
                widthChanged ? _widthText.Text : _heightText.Text,
                ratio,
                widthChanged,
                out var scaled))
        {
            var suppressed = _suppressResolutionEvents;
            _suppressResolutionEvents = true;
            if (widthChanged)
                _heightText.Text = scaled.ToString();
            else
                _widthText.Text = scaled.ToString();
            _suppressResolutionEvents = suppressed;
        }

        SelectPreset(UserSettings.Custom);
        ValidateResolution();
    }

    private void OnAspectLockChanged()
    {
        if (_suppressResolutionEvents)
            return;

        if (_aspectLockButton.IsChecked == true && TryReadMode(out var mode))
        {
            _lockedAspectRatio = new ResolutionSize(mode.Width, mode.Height);
        }
        else
        {
            _lockedAspectRatio = null;
            if (_aspectLockButton.IsChecked == true)
            {
                _suppressResolutionEvents = true;
                _aspectLockButton.IsChecked = false;
                _suppressResolutionEvents = false;
            }
        }

        UpdateAspectLockButton();
    }

    private static bool TryScaleLockedDimension(
        string text,
        ResolutionSize ratio,
        bool widthChanged,
        out uint scaled)
    {
        scaled = 0;
        if (!uint.TryParse(text.Trim(), out var input))
            return false;

        var value = widthChanged
            ? input * (double)ratio.Height / ratio.Width
            : input * (double)ratio.Width / ratio.Height;
        if (value < 1 || value > uint.MaxValue)
            return false;

        scaled = (uint)Math.Round(value, MidpointRounding.AwayFromZero);
        return true;
    }

    private void UpdateAspectLockButton()
    {
        var locked = _aspectLockButton.IsChecked == true && _lockedAspectRatio is not null;
        var accessibleName = locked
            ? "Unlock aspect ratio"
            : "Lock aspect ratio";
        _aspectLockButton.Content = locked ? "\uE72E" : "\uE785";
        _aspectLockButton.ToolTip = accessibleName;
        AutomationProperties.SetName(_aspectLockButton, accessibleName);
    }

    private void SetModeText(DisplayMode mode, bool copyRefresh)
    {
        var suppressed = _suppressResolutionEvents;
        _suppressResolutionEvents = true;
        _widthText.Text = mode.Width.ToString();
        _heightText.Text = mode.Height.ToString();
        if (copyRefresh)
            _refreshCombo.SelectedItem = mode.RefreshHz;
        if (_aspectLockButton.IsChecked == true)
            _lockedAspectRatio = new ResolutionSize(mode.Width, mode.Height);
        _suppressResolutionEvents = suppressed;
    }

    private void SelectPreset(string key)
    {
        var suppressed = _suppressResolutionEvents;
        _suppressResolutionEvents = true;
        _presetCombo.SelectedItem = _presetCombo.Items
            .Cast<PresetChoice>()
            .Single(item => item.Key == key);
        _suppressResolutionEvents = suppressed;
    }

    private uint SelectedRefresh() => _refreshCombo.SelectedItem is uint value ? value : 60;

    private bool TryReadMode(out DisplayMode mode)
    {
        var valid = ResolutionOptions.TryParseMode(
            _widthText.Text,
            _heightText.Text,
            SelectedRefresh(),
            out mode,
            out var widthError,
            out var heightError);
        SetValidation(_widthText, widthError);
        SetValidation(_heightText, heightError);
        return valid;
    }

    private void SetValidation(TextBox textBox, string? message)
    {
        textBox.ToolTip = string.IsNullOrEmpty(message) ? null : message;
        if (string.IsNullOrEmpty(message))
            textBox.ClearValue(Control.BorderBrushProperty);
        else
            textBox.BorderBrush = (Brush)FindResource("ErrorBrush");
    }

    private void ValidateResolution(bool persist = true)
    {
        _modeValid = TryReadMode(out var mode);
        if (_modeValid && _presetCombo.SelectedItem is PresetChoice choice)
        {
            _lastValidSettings = new(
                choice.Key,
                mode.Width,
                mode.Height,
                mode.RefreshHz,
                _primaryCheck.IsChecked == true,
                _routingCheck.IsChecked == true,
                _lastValidSettings.MinimizeToNotificationArea,
                _lastValidSettings.CloseToNotificationArea);
            if (persist)
                PersistSettings();
        }
        UpdateStartStopEnabled();
    }

    private void UpdatePersistedChecks()
    {
        _lastValidSettings = _lastValidSettings with
        {
            MakePrimary = _primaryCheck.IsChecked == true,
            RouteNewWindows = _routingCheck.IsChecked == true
        };
        PersistSettings();
    }

    private void PersistSettings()
    {
        try
        {
            _saveSettings(_lastValidSettings);
        }
        catch (Exception exception)
        {
            SetStatusError($"Settings save failed: {exception.Message}");
        }
    }

    private void UpdateStartStopEnabled() =>
        _startStopButton.IsEnabled = !_busy &&
            (_session is not null || (_driverStatus.Kind == DriverStatusKind.Ready && _modeValid));

    internal bool StartWithWindowsEnabled => _startWithWindowsEnabled;
    internal bool MinimizeToNotificationAreaEnabled =>
        _lastValidSettings.MinimizeToNotificationArea;
    internal bool CloseToNotificationAreaEnabled =>
        _lastValidSettings.CloseToNotificationArea;

    internal void SetStartWithWindows(bool enabled)
    {
        _setStartWithWindows(enabled);
        _startWithWindowsEnabled = enabled;
    }

    internal void SetMinimizeToNotificationArea(bool enabled) =>
        SaveApplicationSettings(_lastValidSettings with { MinimizeToNotificationArea = enabled });

    internal void SetCloseToNotificationArea(bool enabled) =>
        SaveApplicationSettings(_lastValidSettings with { CloseToNotificationArea = enabled });

    private void SaveApplicationSettings(UserSettings settings)
    {
        _saveSettings(settings);
        _lastValidSettings = settings;
    }

    internal void HideToNotificationArea()
    {
        try
        {
            _notificationAreaIcon ??= new NotificationAreaIcon(
                this,
                RestoreFromNotificationArea,
                GetNotificationAreaAction,
                ToggleFromNotificationArea,
                ExitApplication);
            _notificationAreaIcon.Show();
            ShowInTaskbar = false;
            Hide();
        }
        catch (Exception exception)
        {
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            if (!IsVisible)
                Show();
            SetStatusError($"Could not minimize to notification area: {exception.Message}");
        }
    }

    private void RestoreFromNotificationArea()
    {
        _notificationAreaIcon?.Hide();
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    private void OnWindowStateChanged()
    {
        if (WindowState == WindowState.Minimized && MinimizeToNotificationAreaEnabled)
            Dispatcher.BeginInvoke(HideToNotificationArea);
    }

    internal static (string Label, bool Enabled) NotificationAreaAction(
        bool active,
        bool transitioning,
        bool modeValid,
        bool driverReady = true) =>
        (
            active ? "Stop virtual display" : "Start virtual display",
            !transitioning && (active || (driverReady && modeValid))
        );

    private (string Label, bool Enabled) GetNotificationAreaAction() =>
        NotificationAreaAction(
            _session is not null,
            _transitioning,
            _modeValid,
            _driverStatus.Kind == DriverStatusKind.Ready);

    private async void ToggleFromNotificationArea()
    {
        if (GetNotificationAreaAction().Enabled)
            await ToggleAsync();
    }


    private async Task RecoverSessionAsync()
    {
        if (_recoveryAttempted)
            return;
        _recoveryAttempted = true;

        if (_driverStatus.Kind != DriverStatusKind.Ready)
            return;
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_session is not null)
                return;

            _transitioning = true;
            SetUiState("Recovering...", true, false);
            SudoVdaClient? driver = null;
            CancellationTokenSource? watchdogCancellation = null;
            Task watchdogTask = Task.CompletedTask;
            WindowRouter? router = null;

            try
            {
                driver = SudoVdaClient.Open();
                driver.Ping();
                if (!DisplayController.TryGetOwnedDisplayName(
                        MonitorGuid, out var deviceName))
                {
                    driver.Dispose();
                    driver = null;
                    SetUiState("Stopped", false, false);
                    return;
                }

                var active = DisplayController.Capture();
                var activeDisplay = active.Displays.Single(display =>
                    string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
                var snapshot = DisplayController.CreateFallbackRecoverySnapshot(active, deviceName);

                var watchdog = driver.GetWatchdog();
                watchdogCancellation = new CancellationTokenSource();
                watchdogTask = RunWatchdogAsync(
                    driver, watchdog.Timeout, watchdogCancellation.Token);
                var bounds = DisplayController.GetBounds(deviceName);
                if (_routingCheck.IsChecked == true)
                    router = WindowRouter.Start(bounds, ReportBackgroundError);

                _session = new MonitorSession(
                    MonitorGuid,
                    driver,
                    snapshot,
                    deviceName,
                    watchdogCancellation,
                    watchdogTask,
                    router);
                driver = null;
                watchdogCancellation = null;
                router = null;
                SetUiState($"Active: {deviceName} — {activeDisplay.Mode}", false, true);
            }
            catch (Exception exception)
            {
                var errors = new List<string> { exception.Message };
                await TryCleanupAsync(async () =>
                {
                    if (router is not null)
                        await router.DisposeAsync();
                }, "stop recovered routing", errors);
                watchdogCancellation?.Cancel();
                await TryCleanupAsync(
                    () => watchdogTask,
                    "stop recovered watchdog",
                    errors);
                watchdogCancellation?.Dispose();
                driver?.Dispose();
                SetUiState($"Recovery failed: {string.Join(" | ", errors)}", false, false);
            }
        }
        finally
        {
            _transitioning = false;
            _lifecycleGate.Release();
        }
    }

    private async Task ToggleAsync()
    {
        if (_session is null)
            await StartAsync();
        else
            await StopAsync();
    }

    private async Task StartAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_session is not null)
                return;

            if (!TryReadMode(out var mode))
            {
                ValidateResolution();
                return;
            }

            _transitioning = true;
            SetUiState("Starting...", true, false);

            DisplaySnapshot? snapshot = null;
            SudoVdaClient? driver = null;
            CancellationTokenSource? watchdogCancellation = null;
            Task watchdogTask = Task.CompletedTask;
            WindowRouter? router = null;
            var added = false;

            try
            {
                snapshot = DisplayController.Capture();
                if (!DisplayController.IsSupported(mode))
                    throw new InvalidOperationException($"Unsupported display mode: {mode}.");

                driver = SudoVdaClient.Open();
                var watchdog = driver.GetWatchdog();
                var addedDisplay = driver.Add(mode, MonitorGuid);
                added = true;

                watchdogCancellation = new CancellationTokenSource();
                watchdogTask = RunWatchdogAsync(driver, watchdog.Timeout, watchdogCancellation.Token);

                var deviceName = await DisplayController.WaitForDisplayAsync(
                    addedDisplay,
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None);
                var bounds = DisplayController.PlaceAndSetPrimary(
                    deviceName, mode, _primaryCheck.IsChecked == true);

                if (_routingCheck.IsChecked == true)
                    router = WindowRouter.Start(bounds, ReportBackgroundError);

                _session = new MonitorSession(
                    MonitorGuid,
                    driver,
                    snapshot,
                    deviceName,
                    watchdogCancellation,
                    watchdogTask,
                    router);

                driver = null;
                watchdogCancellation = null;
                router = null;
                SetUiState($"Active: {deviceName} — {mode}", false, true);
            }
            catch (Exception exception)
            {
                var cleanupErrors = await CleanupPartialStartAsync(
                    snapshot,
                    driver,
                    added,
                    watchdogCancellation,
                    watchdogTask,
                    router);
                var suffix = cleanupErrors.Count == 0
                    ? string.Empty
                    : $" Cleanup: {string.Join(" | ", cleanupErrors)}";
                SetUiState($"Start failed: {exception.Message}{suffix}", false, false);
            }
        }
        finally
        {
            _transitioning = false;
            _lifecycleGate.Release();
        }
    }

    private async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            var session = _session;
            if (session is null)
                return;

            _transitioning = true;
            SetUiState("Stopping...", true, false);
            var errors = new List<string>();
            var removed = false;

            await TryCleanupAsync(async () =>
            {
                if (session.Router is not null)
                    await session.Router.DisposeAsync();
            }, "stop window routing", errors);
            await TryCleanupAsync(() =>
            {
                var originalPrimary = session.Snapshot.Displays.Single(display => display.Primary);
                WindowRouter.RelocateWindows(
                    DisplayController.GetBounds(session.DeviceName),
                    DisplayController.GetBounds(originalPrimary.DeviceName),
                    errors.Add);
                return Task.CompletedTask;
            }, "relocate windows", errors);

            await TryCleanupAsync(() =>
            {
                DisplayController.Restore(session.Snapshot);
                return Task.CompletedTask;
            }, "restore display topology", errors);

            session.WatchdogCancellation.Cancel();
            await TryCleanupAsync(
                () => session.WatchdogTask,
                "stop watchdog",
                errors);
            try
            {
                session.Driver.Remove(session.MonitorGuid);
                removed = true;
            }
            catch (Exception exception)
            {
                errors.Add($"remove virtual display: {exception.Message}");
            }
            if (removed)
            {
                session.WatchdogCancellation.Dispose();
                session.Driver.Dispose();
                _session = null;
                SetUiState(
                    errors.Count == 0 ? "Stopped" : $"Stopped with errors: {string.Join(" | ", errors)}",
                    false,
                    false);
            }
            else
            {
                SetUiState($"Stop failed: {string.Join(" | ", errors)}", false, true, true);
            }
        }
        finally
        {
            _transitioning = false;
            _lifecycleGate.Release();
        }
    }

    private static async Task<List<string>> CleanupPartialStartAsync(
        DisplaySnapshot? snapshot,
        SudoVdaClient? driver,
        bool added,
        CancellationTokenSource? watchdogCancellation,
        Task watchdogTask,
        WindowRouter? router)
    {
        var errors = new List<string>();

        await TryCleanupAsync(async () =>
        {
            if (router is not null)
                await router.DisposeAsync();
        }, "stop window routing", errors);

        if (snapshot is not null)
        {
            await TryCleanupAsync(() =>
            {
                DisplayController.Restore(snapshot);
                return Task.CompletedTask;
            }, "restore display topology", errors);
        }

        watchdogCancellation?.Cancel();
        await TryCleanupAsync(() => watchdogTask, "stop watchdog", errors);

        if (added && driver is not null)
        {
            await TryCleanupAsync(() =>
            {
                driver.Remove(MonitorGuid);
                return Task.CompletedTask;
            }, "remove virtual display", errors);
        }

        watchdogCancellation?.Dispose();
        driver?.Dispose();
        return errors;
    }

    private async Task RunWatchdogAsync(
        SudoVdaClient driver,
        uint timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (timeoutSeconds == 0)
            return;

        var interval = TimeSpan.FromMilliseconds(Math.Max(250, timeoutSeconds * 1000d / 3d));
        try
        {
            while (true)
            {
                await Task.Delay(interval, cancellationToken);
                driver.Ping();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportBackgroundError($"SudoVDA watchdog failed: {exception.Message}");
        }
    }

    private static async Task TryCleanupAsync(
        Func<Task> action,
        string operation,
        ICollection<string> errors)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            errors.Add($"{operation}: {exception.Message}");
        }
    }

    private void ReportBackgroundError(string message)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ReportBackgroundError(message));
            return;
        }

        SetStatusError(message);
    }

    internal void SetUiState(string status, bool busy, bool active, bool error = false)
    {
        _statusLabel.Text = status;
        var brushKey = error
            ? "ErrorBrush"
            : busy
                ? "BusyBrush"
                : active
                    ? "ActiveBrush"
                    : "ErrorBrush";
        _statusIndicator.Foreground = (Brush)FindResource(brushKey);
        _busy = busy;
        var resolutionEnabled = !busy && !active;
        _aspectCombo.IsEnabled = resolutionEnabled;
        _aspectLockButton.IsEnabled = resolutionEnabled;
        _presetCombo.IsEnabled = resolutionEnabled;
        _widthText.IsEnabled = resolutionEnabled;
        _heightText.IsEnabled = resolutionEnabled;
        _refreshCombo.IsEnabled = resolutionEnabled;
        _primaryCheck.IsEnabled = resolutionEnabled;
        _routingCheck.IsEnabled = resolutionEnabled;
        _startStopButton.Content = active ? "Stop" : "Start";
        UpdateStartStopEnabled();
    }

    private void SetStatusError(string status)
    {
        _statusLabel.Text = status;
        _statusIndicator.Foreground = (Brush)FindResource("ErrorBrush");
    }

    private void OnWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (!_allowClose && !_exitRequested && CloseToNotificationAreaEnabled)
        {
            eventArgs.Cancel = true;
            HideToNotificationArea();
            return;
        }

        if (_allowClose || (_session is null && !_transitioning))
            return;

        eventArgs.Cancel = true;
        if (_closing)
            return;

        _closing = true;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            await StopAsync();
            if (_session is null)
            {
                _allowClose = true;
                Close();
            }
            else
            {
                _closing = false;
            }
        }));
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
    private sealed record AspectFilterChoice(string Label, ResolutionAspectRatio? Ratio)
    {
        public override string ToString() => Label;
    }

    private sealed record PresetChoice(string Label, string Key, ResolutionSize? Size)
    {
        public override string ToString() => Label;
    }

    private sealed record MonitorSession(
        Guid MonitorGuid,
        SudoVdaClient Driver,
        DisplaySnapshot Snapshot,
        string DeviceName,
        CancellationTokenSource WatchdogCancellation,
        Task WatchdogTask,
        WindowRouter? Router);
}
