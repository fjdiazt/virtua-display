using Microsoft.Win32;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Virtua.Display;

internal static class SelfTest
{
    private static int _failures;

    internal static int Run()
    {
        Check(!Assembly.GetExecutingAssembly().GetReferencedAssemblies()
            .Any(reference => reference.Name == "System.Windows.Forms"),
            "no Windows Forms assembly reference");
        Check(Assembly.GetExecutingAssembly().GetName().Name == "VirtuaDisplay", "assembly name");
        CheckSingleInstance();
        Check(StartupRegistration.BuildCommand(@"C:\Apps\VirtuaDisplay.exe") ==
              "\"C:\\Apps\\VirtuaDisplay.exe\" --startup",
            "startup command");
        Check(MainWindow.NotificationAreaAction(false, false, true) ==
              ("Start virtual display", true), "tray start command");
        Check(MainWindow.NotificationAreaAction(true, false, true) ==
              ("Stop virtual display", true), "tray stop command");
        Check(MainWindow.NotificationAreaAction(false, true, true) ==
              ("Start virtual display", false), "tray command disabled while transitioning");
        Check(MainWindow.NotificationAreaAction(false, false, true, false) ==
              ("Start virtual display", false), "tray start disabled without driver");
        Check(new DisplayMode(1920, 1080, 60).ToString() == "1920 x 1080 @ 60 Hz", "display mode formatting");
        CheckResolutionSettings();
        CheckResolutionWindow();
        CheckAspectLockWindow();
        CheckSmokeWindow();

        Check(SudoVdaClient.IoctlAdd == 0x00222000, "ADD IOCTL");
        Check(SudoVdaClient.IoctlRemove == 0x00222004, "REMOVE IOCTL");
        Check(SudoVdaClient.IoctlGetWatchdog == 0x0022200C, "watchdog IOCTL");
        Check(SudoVdaClient.IoctlPing == 0x00222220, "ping IOCTL");
        Check(SudoVdaClient.IoctlGetProtocol == 0x002223FC, "protocol IOCTL");
        Check(Marshal.SizeOf<SudoVdaClient.AddParams>() == 56, "ADD layout");
        Check(Marshal.SizeOf<SudoVdaClient.AddOut>() == 12, "ADD output layout");
        Check(Marshal.SizeOf<SudoVdaClient.ProtocolVersion>() == 4, "protocol layout");
        Check(DriverStatus.FromProtocol(0, 2, 0).Kind == DriverStatusKind.Ready,
            "minimum driver protocol ready");
        Check(DriverStatus.FromProtocol(0, 1, 9).Kind == DriverStatusKind.Incompatible,
            "old driver protocol incompatible");
        Check(DriverStatus.FromProtocol(1, 0, 0).Kind == DriverStatusKind.Incompatible,
            "different driver major incompatible");
        Check((int)DriverStatusKind.Ready == 0 &&
              (int)DriverStatusKind.Missing == 2 &&
              (int)DriverStatusKind.Incompatible == 3 &&
              (int)DriverStatusKind.Error == 4,
            "driver status exit codes");

        var modes = DisplayController.DistinctModes(
        [
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(2560, 1440, 120)
        ]);
        Check(modes.Count == 2, "mode deduplication");
        Check(modes[0].Width == 1920 && modes[1].Width == 2560, "mode ordering");
        Check(DisplayController.IsSupported(new DisplayMode(640, 480, 60)), "minimum mode");
        Check(!DisplayController.IsSupported(new DisplayMode(639, 480, 60)), "below-minimum mode");

        var recoveryOwner = Guid.NewGuid();
        Check(DisplayController.MatchesRecoveryOwner(recoveryOwner, recoveryOwner),
            "session recovery exact ownership");
        Check(!DisplayController.MatchesRecoveryOwner(recoveryOwner, Guid.NewGuid()),
            "session recovery container mismatch");

        var orphanedSnapshot = DisplayController.CreateFallbackRecoverySnapshot(
            new DisplaySnapshot(
            [
                new DisplayState("virtual", new Point(0, 0), new DisplayMode(1920, 1080, 60), true),
                new DisplayState("physical-a", new Point(0, 1080), new DisplayMode(1920, 1080, 60), false),
                new DisplayState("physical-b", new Point(1920, 1080), new DisplayMode(1280, 1024, 60), false)
            ]),
            "virtual");
        Check(orphanedSnapshot.Displays.SequenceEqual(
        [
            new DisplayState("physical-a", new Point(0, 0), new DisplayMode(1920, 1080, 60), true),
            new DisplayState("physical-b", new Point(1920, 0), new DisplayMode(1280, 1024, 60), false)
        ]), "orphan recovery synthesizes physical topology");

        var snapshot = DisplayController.Capture();
        Check(snapshot.Displays.Count > 0, "active display discovery");

        var placedDisplays = new DisplaySnapshot(
        [
            new DisplayState("physical", new Point(100, 200), new DisplayMode(1920, 1080, 60), true),
            new DisplayState("virtual", new Point(2020, 200), new DisplayMode(1280, 720, 60), false)
        ]);
        Check(DisplayController.ChoosePosition(placedDisplays, "virtual") == new Point(420, -520),
            "center virtual display above offset primary");

        var overlappingDisplays = new DisplaySnapshot(
        [
            new DisplayState("physical", new Point(0, 0), new DisplayMode(1920, 1080, 60), true),
            new DisplayState("virtual", new Point(0, 0), new DisplayMode(1920, 1080, 60), false)
        ]);
        Check(DisplayController.ChoosePosition(overlappingDisplays, "virtual") == new Point(0, -1080),
            "center overlapping virtual display above primary");
        Check(snapshot.Displays.Count(display => display.Primary) == 1, "single primary discovery");
        var modeChoices = DisplayController.GetModeChoices();
        Check(modeChoices.Count > 0, "display mode discovery");
        Check(new[] { 1080u, 1440u, 2160u }.All(width =>
            modeChoices.Any(mode => mode.Width == width && mode.Height == width)), "square fallback modes");

        Check(WindowRouter.IsEligible(new(true, false, false, false, 42), 7, 99), "normal window");
        Check(!WindowRouter.IsEligible(new(true, false, false, false, 7), 7, 99), "own process");
        Check(!WindowRouter.IsEligible(new(true, true, false, false, 42), 7, 99), "cloaked window");
        Check(!WindowRouter.IsEligible(new(true, false, true, false, 42), 7, 99), "tool window");
        Check(!WindowRouter.IsEligible(new(true, false, false, true, 42), 7, 99), "non-activating window");
        Check(!WindowRouter.IsEligible(new(true, false, false, false, 99), 7, 99), "shell process");
        Check(!WindowRouter.IsEligible(new(false, false, false, false, 42), 7, 99), "hidden window");
        Check(WindowRouter.ShouldRelocate(
                new WindowCandidate(true, false, false, false, 42), true, false, true),
            "normal source-display window relocates");
        Check(!WindowRouter.ShouldRelocate(
                new WindowCandidate(true, false, false, false, 42), true, true, true),
            "shell window does not relocate");
        Check(!WindowRouter.ShouldRelocate(
                new WindowCandidate(true, false, false, false, 42), true, false, false),
            "other-display window does not relocate");

        var sourceBounds = new Rectangle(0, -1080, 1920, 1080);
        var destinationBounds = new Rectangle(100, 200, 1280, 720);
        Check(WindowRouter.CalculateRelocatedBounds(
                new Rectangle(200, -980, 800, 600), sourceBounds, destinationBounds) ==
              new Rectangle(300, 300, 800, 600),
            "relocation preserves relative offset");
        Check(WindowRouter.CalculateRelocatedBounds(
                new Rectangle(1500, -200, 900, 700), sourceBounds, destinationBounds) ==
              new Rectangle(480, 220, 900, 700),
            "relocation clamps inside destination");
        Check(WindowRouter.CalculateRelocatedBounds(
                new Rectangle(-100, -1200, 2000, 1000), sourceBounds, destinationBounds) ==
              destinationBounds,
            "relocation shrinks oversized window");
        Check(typeof(WindowRouter).GetMethod(
                "RelocateWindows",
                BindingFlags.Static | BindingFlags.NonPublic) is not null,
            "stop window relocation entry point");
        Check(typeof(MainWindow).GetMethod(
                "RecoverSessionAsync",
                BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "startup session recovery entry point");

        using var driver = SudoVdaClient.Open();
        var protocol = driver.GetProtocolVersion();
        var watchdog = driver.GetWatchdog();
        Check(protocol.Major == 0 && protocol.Minor >= 2, "installed driver protocol");
        Check(watchdog.Countdown <= watchdog.Timeout, "installed driver watchdog");
        Console.WriteLine($"SudoVDA protocol {protocol.Major}.{protocol.Minor}.{protocol.Incremental}; watchdog {watchdog.Timeout}s.");

        if (_failures == 0)
        {
            Console.WriteLine("Self-test passed.");
            return 0;
        }

        Console.Error.WriteLine($"Self-test failed: {_failures} check(s).");
        return 1;
    }

    private static void CheckSingleInstance()
    {
        var name = $@"Local\Virtua.Display.Tests.{Guid.NewGuid():N}";
        var first = App.TryAcquireSingleInstance(name);
        var second = App.TryAcquireSingleInstance(name);

        Check(first is not null, "first application instance owns mutex");
        Check(second is null, "second application instance is rejected");

        if (second is not null)
        {
            second.ReleaseMutex();
            second.Dispose();
        }

        if (first is not null)
        {
            first.ReleaseMutex();
            first.Dispose();
        }
    }

    private static void CheckResolutionWindow()
    {
        var primary = new DisplayMode(3440, 1440, 119);
        UserSettings? saved = null;
        bool? startWithWindowsSaved = null;
        var window = new MainWindow(
            primary,
            UserSettings.Defaults(primary),
            [
                primary,
                new DisplayMode(1920, 1080, 60),
                new DisplayMode(1920, 1200, 60),
                new DisplayMode(1024, 768, 60),
                new DisplayMode(1280, 1024, 60),
                new DisplayMode(1000, 1000, 60)
            ],
            value => saved = value,
            false,
            value => startWithWindowsSaved = value);

        var preset = Find<ComboBox>(window, "_presetCombo");
        var aspect = Find<ComboBox>(window, "_aspectCombo");
        var width = Find<TextBox>(window, "_widthText");
        var height = Find<TextBox>(window, "_heightText");
        var refresh = Find<ComboBox>(window, "_refreshCombo");
        var primaryCheck = Find<CheckBox>(window, "_primaryCheck");
        var routingCheck = Find<CheckBox>(window, "_routingCheck");
        var startWithWindowsCheck = window.FindName("_startWithWindowsCheck") as CheckBox;
        var minimizeCheck = window.FindName("_minimizeToNotificationAreaCheck") as CheckBox;
        var closeCheck = window.FindName("_closeToNotificationAreaCheck") as CheckBox;
        var start = Find<Button>(window, "_startStopButton");
        var appTitle = window.FindName("appTitle") as TextBlock;
        var appSubtitle = window.FindName("appSubtitle") as TextBlock;
        var displaySection = window.FindName("displaySection") as Border;
        var behaviorSection = window.FindName("behaviorSection") as Border;
        var applicationSection = window.FindName("applicationSection") as Border;
        var resolutionLayout = Find<Grid>(window, "resolutionLayout");
        var widthLabel = Find<Label>(window, "widthLabel");
        var heightLabel = Find<Label>(window, "heightLabel");
        var refreshLabel = Find<Label>(window, "refreshLabel");
        var statusIndicator = Find<TextBlock>(window, "_statusIndicator");

        Check(window.Title == "Virtua Display", "main window title");
        Check(window.ResizeMode == System.Windows.ResizeMode.CanMinimize, "minimize button available");
        var trayMenu = NotificationAreaIcon.CreateMenu(
            () => { },
            () => ("Start virtual display", true),
            () => { },
            () => { });
        Check(trayMenu.Placement == PlacementMode.MousePoint,
            "tray menu uses top-level WPF popup");
        Check(trayMenu.Items.Cast<object>().OfType<MenuItem>()
            .Select(item => item.Header?.ToString()).SequenceEqual(
            ["Open Virtua Display", "Start virtual display", "Exit"]),
            "tray menu commands");
        trayMenu.ApplyTemplate();
        Check(trayMenu.Template.FindName("DarkMenuBackground", trayMenu) is Border,
            "dark tray menu chrome");
        Check(Equals(window.Background, window.FindResource("WindowBackgroundBrush")),
            "dark window theme");
        Check(window.FontFamily.Source == "Segoe UI Variable Text", "modern window typography");
        Check(appTitle?.Text == "Virtua Display" && appTitle.FontSize == 28,
            "modern app heading");
        Check(appSubtitle?.Text == "One focused virtual display.", "app subtitle");
        Check(displaySection is not null && behaviorSection is not null &&
              applicationSection is not null,
            "divider-based sections");
        Check(window.FindName("displayGroup") is null &&
              window.FindName("behaviorGroup") is null &&
              window.FindName("applicationBehaviorGroup") is null,
            "legacy group boxes removed");
        aspect.ApplyTemplate();
        width.ApplyTemplate();
        start.ApplyTemplate();
        primaryCheck.ApplyTemplate();
        Check(aspect.Template.FindName("ComboBorder", aspect) is Border comboBorder &&
              Equals(comboBorder.Background, window.FindResource("ControlBackgroundBrush")),
            "modern combo box chrome");
        Check(width.Template.FindName("InputBorder", width) is Border,
            "modern text box chrome");
        Check(start.Template.FindName("ButtonBorder", start) is Border,
            "modern primary button chrome");
        Check(primaryCheck.Template.FindName("CheckBorder", primaryCheck) is Border,
            "modern check box chrome");
        Check(aspect.SelectedItem?.ToString() == "All aspect ratios", "all-aspects default");
        Check(preset.SelectedItem?.ToString() == "Match primary display", "match-primary default");
        Check(width.Text == "3440" && height.Text == "1440", "copy-primary dimensions");
        Check((uint)refresh.SelectedItem! == 119, "copy-primary refresh");
        Check(aspect.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(
        [
            "All aspect ratios",
            "1:1 (Square)",
            "4:3 (Standard)",
            "5:4 (Standard)",
            "16:9 (Wide)",
            "16:10 (Wide)",
            "21:9 (Ultrawide)"
        ]), "aspect dropdown contents");
        Check(preset.Items.Cast<object>().Any(item => item.ToString() ==
            "1920 x 1080 (16:9 Wide)"), "all-aspects preset annotation");

        aspect.SelectedItem = aspect.Items.Cast<object>()
            .Single(item => item.ToString() == "16:9 (Wide)");
        Check(preset.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(
        [
            "Match primary display",
            "1920 x 1080",
            "Custom"
        ]), "filtered preset contents");
        Check(preset.SelectedItem?.ToString() == "1920 x 1080",
            "filter selects first matching preset");
        aspect.SelectedIndex = 0;
        Check(preset.SelectedItem?.ToString() == "1920 x 1080 (16:9 Wide)",
            "all-aspects preserves selected preset");
        Check(primaryCheck.IsChecked == true, "make-primary default");
        Check(routingCheck.IsChecked == true, "routing default");
        Check(startWithWindowsCheck?.Content?.ToString() == "Start with Windows",
            "start-with-Windows option");
        Check(startWithWindowsCheck?.IsChecked == false, "start-with-Windows default");
        Check(minimizeCheck?.Content?.ToString() == "Minimize to notification area",
            "notification-area option");
        Check(closeCheck?.Content?.ToString() == "Close to notification area",
            "close-to-notification-area option");
        Check(closeCheck?.IsChecked == false, "close-to-notification-area default");
        Check(start.Content?.ToString() == "Start", "start button default");
        Check(displaySection?.BorderThickness.Bottom == 1, "display section divider");
        Check(behaviorSection?.BorderThickness.Bottom == 1, "behavior section divider");
        Check(applicationSection?.BorderThickness.Bottom == 1,
            "application section divider");
        Check(Grid.GetColumn(width) == 0 && Grid.GetRow(width) == 5 &&
              Grid.GetColumn(height) == 1 && Grid.GetRow(height) == 5 &&
              Grid.GetColumn(refresh) == 3 && Grid.GetRow(refresh) == 5,
            "dimension controls share one row");
        Check(Grid.GetRow(widthLabel) == 4 &&
              Grid.GetRow(heightLabel) == 4 &&
              Grid.GetRow(refreshLabel) == 4 &&
              Grid.GetColumn(refreshLabel) == 3,
            "dimension labels share row above controls");
        Check(Equals(statusIndicator.Foreground, window.FindResource("ErrorBrush")),
            "stopped status color");
        window.SetUiState("Starting...", true, false);
        Check(Equals(statusIndicator.Foreground, window.FindResource("BusyBrush")),
            "busy status color");
        window.SetUiState("Active", false, true);
        Check(Equals(statusIndicator.Foreground, window.FindResource("ActiveBrush")),
            "active status color");
        window.SetUiState("Stop failed", false, true, true);
        Check(Equals(statusIndicator.Foreground, window.FindResource("ErrorBrush")),
            "error status color");
        window.SetUiState("Stopped", false, false);

        preset.SelectedItem = preset.Items.Cast<object>()
            .Single(item => item.ToString() == "1920 x 1080 (16:9 Wide)");
        Check(width.Text == "1920" && height.Text == "1080", "preset populates dimensions");
        width.Text = "2000";
        Check(preset.SelectedItem?.ToString() == "Custom", "manual edit selects custom");
        Check(saved?.Width == 2000, "valid resolution change saves immediately");
        width.Text = "invalid";
        Check(!start.IsEnabled && width.ToolTip?.ToString() == "Width must be 640–7680.",
            "invalid width disables start and explains error");
        Check(saved?.Width == 2000, "invalid resolution is not saved");
        width.Text = "2000";
        window.SetUiState("Active", false, true);
        Check(!aspect.IsEnabled && !preset.IsEnabled && !width.IsEnabled &&
              !height.IsEnabled && !refresh.IsEnabled,
            "active display locks resolution controls");
        window.SetUiState("Stopped", false, false);
        Check(aspect.IsEnabled && preset.IsEnabled && width.IsEnabled &&
              height.IsEnabled && refresh.IsEnabled,
            "stopped display unlocks resolution controls");
        Check(start.IsEnabled, "valid width enables start");

        primaryCheck.IsChecked = false;
        routingCheck.IsChecked = false;
        startWithWindowsCheck!.IsChecked = true;
        if (minimizeCheck is not null)
            minimizeCheck.IsChecked = true;
        if (closeCheck is not null)
            closeCheck.IsChecked = true;
        Check(startWithWindowsSaved == true, "start-with-Windows registration");
        Check(saved == new UserSettings("Custom", 2000, 1080, 119, false, false, true, true),
            "window settings persistence");
        Check(saved?.GetType().GetProperty("MinimizeToNotificationArea")?.GetValue(saved) is true,
            "notification-area preference persistence");
        width.Text = "2100";
        Check(saved?.CloseToNotificationArea == true,
            "close-to-notification-area preference persistence");
        Check(saved?.Width == 2100, "later resolution change saves immediately");
        closeCheck!.IsChecked = false;
        window.Close();

        const string missingMessage = "SudoVDA driver is missing.";
        var missingWindow = new MainWindow(
            primary,
            UserSettings.Defaults(primary),
            [primary],
            _ => { },
            driverStatus: new(DriverStatusKind.Missing, missingMessage));
        Check(!Find<Button>(missingWindow, "_startStopButton").IsEnabled,
            "missing driver disables start");
        Check(Find<TextBlock>(missingWindow, "_statusLabel").Text == missingMessage,
            "missing driver status shown");
        missingWindow.Close();
    }

    private static void CheckAspectLockWindow()
    {
        var primary = new DisplayMode(1920, 1080, 60);
        var window = new MainWindow(
            primary,
            UserSettings.Defaults(primary),
            [primary, new DisplayMode(1920, 1200, 60)],
            _ => { });

        var preset = Find<ComboBox>(window, "_presetCombo");
        var width = Find<TextBox>(window, "_widthText");
        var height = Find<TextBox>(window, "_heightText");
        var aspectLock = Find<ToggleButton>(window, "_aspectLockButton");
        var layout = Find<Grid>(window, "resolutionLayout");

        Check(aspectLock.IsChecked != true &&
              aspectLock.Content?.ToString() == "\uE785" &&
              AutomationProperties.GetName(aspectLock) == "Lock aspect ratio",
            "aspect lock default");
        aspectLock.ApplyTemplate();
        Check(aspectLock.Template.FindName("LockButtonBorder", aspectLock) is Border,
            "modern aspect lock chrome");
        Check(aspectLock.FontFamily.Source == "Segoe Fluent Icons",
            "Fluent aspect lock icon");

        Check(layout.ColumnDefinitions.Count == 4 &&
              Grid.GetColumn(width) == 0 &&
              Grid.GetColumn(height) == 1 &&
              Grid.GetColumn(aspectLock) == 2,
            "aspect lock layout");

        aspectLock.IsChecked = true;
        Check(aspectLock.Content?.ToString() == "\uE72E" &&
              AutomationProperties.GetName(aspectLock) == "Unlock aspect ratio",
            "aspect lock enabled");

        width.Text = "2000";
        Check(height.Text == "1125" && preset.SelectedItem?.ToString() == "Custom",
            "locked width updates height");
        height.Text = "1200";
        Check(width.Text == "2133", "locked height updates width");

        preset.SelectedItem = preset.Items.Cast<object>()
            .Single(item => item.ToString() == "1920 x 1200 (16:10 Wide)");
        width.Text = "2000";
        Check(height.Text == "1250", "preset refreshes locked ratio");

        aspectLock.IsChecked = false;
        width.Text = "invalid";
        aspectLock.IsChecked = true;
        Check(aspectLock.IsChecked != true && aspectLock.Content?.ToString() == "\uE785",
            "invalid dimensions refuse aspect lock");

        window.SetUiState("Active", false, true);
        Check(!aspectLock.IsEnabled, "active display locks aspect button");
        window.SetUiState("Stopped", false, false);
        Check(aspectLock.IsEnabled, "stopped display unlocks aspect button");
        window.Close();
    }

    private static void CheckSmokeWindow()
    {
        var window = SmokeTest.CreateTestWindow();
        Check(window.Title == "Virtua Display Smoke Window", "WPF smoke window title");
        Check(window.Width == 640 && window.Height == 480, "WPF smoke window dimensions");
        Check(Marshal.SizeOf<SmokeTest.MonitorInfoEx>() == 104, "monitor info layout");
        window.Close();
    }

    private static T Find<T>(MainWindow window, string name) where T : class =>
        window.FindName(name) as T ??
        throw new InvalidOperationException($"Missing WPF element: {name}.");

    private static void CheckResolutionSettings()
    {
        var primary = new DisplayMode(3440, 1440, 119);
        var rates = ResolutionOptions.RefreshRates(primary.RefreshHz);
        Check(rates.Contains(24u) && rates.Contains(500u), "standard refresh rates");
        Check(rates.Contains(119u), "primary refresh inclusion");

        var square = ResolutionOptions.AspectRatio(new ResolutionSize(1000, 1000));
        Check(square == new ResolutionAspectRatio(1, 1, "Square"), "square aspect classification");
        var ultrawide = ResolutionOptions.AspectRatio(new ResolutionSize(3440, 1440));
        Check(ultrawide == new ResolutionAspectRatio(21, 9, "Ultrawide"),
            "ultrawide aspect classification");
        var unknown = ResolutionOptions.AspectRatio(new ResolutionSize(1000, 700));
        Check(unknown == new ResolutionAspectRatio(10, 7, null), "unknown exact aspect classification");

        var aspectSortedSizes = ResolutionOptions.DistinctSizes(
        [
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(1920, 1200, 60),
            new DisplayMode(1280, 768, 60),
            new DisplayMode(1440, 960, 60),
            new DisplayMode(1024, 768, 60),
            new DisplayMode(1280, 1024, 60),
            new DisplayMode(1000, 1000, 60)
        ]);
        Check(aspectSortedSizes.SequenceEqual(
        [
            new ResolutionSize(1000, 1000),
            new ResolutionSize(1440, 960),
            new ResolutionSize(1024, 768),
            new ResolutionSize(1280, 768),
            new ResolutionSize(1280, 1024),
            new ResolutionSize(1920, 1080),
            new ResolutionSize(1920, 1200)
        ]), "resolution aspect-first ordering");
        var aspects = ResolutionOptions.AspectRatios(aspectSortedSizes);
        Check(aspects.Select(value => value.FilterLabel).SequenceEqual(
        [
            "1:1 (Square)",
            "3:2 (Classic)",
            "4:3 (Standard)",
            "5:3 (Wide)",
            "5:4 (Standard)",
            "16:9 (Wide)",
            "16:10 (Wide)"
        ]), "aspect filter ordering and labels");

        var sizes = ResolutionOptions.DistinctSizes(
        [
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(1920, 1080, 120),
            new DisplayMode(2560, 1440, 120)
        ]);
        Check(sizes.Count == 2, "resolution size deduplication");
        Check(ResolutionOptions.TryParseMode("640", "480", 60, out var minimum, out _, out _) &&
              minimum == new DisplayMode(640, 480, 60), "minimum custom mode");
        Check(!ResolutionOptions.TryParseMode("639", "480", 60, out _, out var widthError, out _) &&
              widthError == "Width must be 640–7680.", "width lower bound");
        Check(!ResolutionOptions.TryParseMode("1920", "nope", 60, out _, out _, out var heightError) &&
              heightError == "Height must be 480–4320.", "nonnumeric height");

        Check(UserSettingsStore.DefaultPath == @"Software\Virtua\Display",
            "settings registry path");
        var path = $@"Software\Virtua\Display\Tests\{Guid.NewGuid():N}";
        try
        {
            var expected = new UserSettings("Custom", 2000, 1000, 144, false, true, true, true);
            UserSettingsStore.Save(expected, path);
            Check(UserSettingsStore.Load(primary, path) == expected, "settings registry round-trip");

            using (var key = Registry.CurrentUser.CreateSubKey(path))
                key.SetValue("SudoVDA GUI", "obsolete", RegistryValueKind.String);
            StartupRegistration.RemoveLegacy(path);
            using (var key = Registry.CurrentUser.OpenSubKey(path))
                Check(key?.GetValue("SudoVDA GUI") is null,
                    "legacy startup registration removed");

            const string fakeExecutable = @"C:\Apps\VirtuaDisplay.exe";
            const string startupValue = "Virtua Display Test";
            Check(!StartupRegistration.IsEnabled(fakeExecutable, path, startupValue),
                "startup registration default");
            StartupRegistration.SetEnabled(true, fakeExecutable, path, startupValue);
            Check(StartupRegistration.IsEnabled(fakeExecutable, path, startupValue),
                "startup registration enabled");
            StartupRegistration.SetEnabled(false, fakeExecutable, path, startupValue);
            Check(!StartupRegistration.IsEnabled(fakeExecutable, path, startupValue),
                "startup registration disabled");
            UserSettingsStore.Save(expected with { Preset = "1920x1080" }, path);
            var mismatchedPreset = UserSettingsStore.Load(primary, path);
            Check(mismatchedPreset.Preset == UserSettings.CopyPrimary,
                "mismatched preset fallback");

            using (var key = Registry.CurrentUser.CreateSubKey(path))
                key.SetValue("Width", 639, RegistryValueKind.DWord);
            var fallback = UserSettingsStore.Load(primary, path);
            Check(fallback.Preset == UserSettings.CopyPrimary && fallback.Width == primary.Width,
                "invalid settings fallback");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
    }

    internal static void Check(bool condition, string name)
    {
        if (condition)
            return;

        _failures++;
        Console.Error.WriteLine($"FAIL: {name}");
    }
}
