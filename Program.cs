using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

[assembly: AssemblyTitle("LOL国际服改中文语音工具")]
[assembly: AssemblyDescription("通过 Riot 启动器切换英雄联盟国际服的语言资源")]
[assembly: AssemblyVersion("1.1.5.0")]
[assembly: AssemblyFileVersion("1.1.5.0")]

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        ConfigureDpi();
        bool acquired;
        using (var instance = new Mutex(true, @"Local\LeagueChineseLocaleLauncher", out acquired))
        {
            if (!acquired)
            {
                var saved = LauncherPreferences.Load(Path.Combine(LauncherPreferences.AppDirectory, "preferences.json"));
                var text = new UiText(saved.UiLanguage);
                MessageBox.Show(text.Get("AlreadyRunning"), text.Get("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var app = new Application();
            var window = LoadWindow();
            new LauncherWindow(window).Initialize();
            app.Run(window);
            instance.ReleaseMutex();
        }
    }

    internal static void ConfigureDpi()
    {
        AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
        AppContext.SetSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier2OrGreater", false);
    }

    internal static Window LoadWindow()
    {
        using (var xaml = Assembly.GetExecutingAssembly().GetManifestResourceStream("Launcher.Main.xaml"))
            return (Window)XamlReader.Load(xaml);
    }
}

internal sealed class LauncherWindow
{
    private readonly Window window;
    private readonly Button start;
    private readonly Button restore;
    private readonly TextBlock title;
    private readonly TextBlock detail;
    private readonly ProgressBar bar;
    private readonly TextBlock progressText;
    private readonly Button settingsButton;
    private readonly Button backButton;
    private readonly Button themeButton;
    private readonly System.Windows.Shapes.Path themeGlyph;
    private readonly FrameworkElement homePanel;
    private readonly FrameworkElement settingsPanel;
    private readonly ScrollViewer pageScroll;
    private readonly TextBlock settingsFeedback;
    private readonly Button autoPathButton;
    private readonly Button manualPathButton;
    private readonly TextBlock installPath;
    private readonly string appDirectory;
    private readonly string preferencesPath;
    private readonly LauncherPreferences preferences;
    private readonly Size minimumWindowSize;
    private readonly double maximumWindowWidth;
    private bool darkTheme;
    private UiText text;
    private LaunchStatus lastStatus;
    private UiMessage installProblem;
    private string detectedGamePath;
    private LeagueLauncher engine;
    private CancellationTokenSource operation;

    public LauncherWindow(Window view, string appDataDirectory = null)
    {
        window = view;
        minimumWindowSize = new Size(window.MinWidth, window.MinHeight);
        maximumWindowWidth = window.MaxWidth;
        appDirectory = appDataDirectory ?? LauncherPreferences.AppDirectory;
        preferencesPath = Path.Combine(appDirectory, "preferences.json");
        preferences = LauncherPreferences.Load(preferencesPath);
        start = (Button)view.FindName("StartButton");
        restore = (Button)view.FindName("RestoreButton");
        title = (TextBlock)view.FindName("StateTitle");
        detail = (TextBlock)view.FindName("StateDetail");
        bar = (ProgressBar)view.FindName("Progress");
        progressText = (TextBlock)view.FindName("ProgressText");
        settingsButton = (Button)view.FindName("SettingsButton");
        backButton = (Button)view.FindName("BackButton");
        themeButton = (Button)view.FindName("ThemeButton");
        themeGlyph = (System.Windows.Shapes.Path)view.FindName("ThemeGlyph");
        homePanel = (FrameworkElement)view.FindName("HomePanel");
        settingsPanel = (FrameworkElement)view.FindName("SettingsPanel");
        pageScroll = (ScrollViewer)view.FindName("PageScroll");
        settingsFeedback = (TextBlock)view.FindName("SettingsFeedback");
        autoPathButton = (Button)view.FindName("AutoPathButton");
        manualPathButton = (Button)view.FindName("ManualPathButton");
        installPath = (TextBlock)view.FindName("InstallPath");
        start.Click += OnStart;
        restore.Click += OnRestore;
        settingsButton.Click += (sender, args) => ShowSettings(true);
        backButton.Click += (sender, args) => ShowSettings(false);
        themeButton.Click += (sender, args) => SetTheme(darkTheme ? ThemeMode.Light : ThemeMode.Dark);
        var systemThemeItem = (MenuItem)view.FindName("SystemThemeItem");
        systemThemeItem.Click += (sender, args) => SetTheme(ThemeMode.System);
        autoPathButton.Click += (sender, args) => SelectGamePath(null);
        manualPathButton.Click += ChooseGamePath;
        ConfigureSettings();
        ApplyUiLanguage();
        ApplyTheme();
        window.SourceInitialized += (sender, args) => {
            ApplyTheme();
            FitWindowToScreen();
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        };
        window.Loaded += (sender, args) => FitWindowToScreen();
        window.LocationChanged += (sender, args) => FitWindowToScreen();
        window.DpiChanged += OnDisplaySettingsChanged;
        window.Activated += (sender, args) => { if (preferences.Theme == ThemeMode.System) ApplyTheme(); };
        window.Closing += (sender, args) => { if (operation != null) operation.Cancel(); };
        window.Closed += (sender, args) => {
            SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        };
    }

    private void ConfigureSettings()
    {
        var chinese = (RadioButton)window.FindName("UiChinese");
        var english = (RadioButton)window.FindName("UiEnglish");
        chinese.IsChecked = preferences.UiLanguage == "zh-CN";
        english.IsChecked = preferences.UiLanguage == "en-US";
        chinese.Checked += (sender, args) => { SetUiLanguage("zh-CN"); AnimateEntrance(chinese); };
        english.Checked += (sender, args) => { SetUiLanguage("en-US"); AnimateEntrance(english); };
    }

    private void SetUiLanguage(string language)
    {
        preferences.UiLanguage = language;
        ApplyUiLanguage();
        SavePreferences();
    }

    private void ApplyUiLanguage()
    {
        text = new UiText(preferences.UiLanguage);
        text.Apply(window);
        if (lastStatus != null) Show(lastStatus);
        UpdateInstallPath();
        Buttons();
    }

    private void ChooseGamePath(object sender, RoutedEventArgs args)
    {
        if (operation != null) return;
        var dialog = new OpenFileDialog {
            Title = text.Get("ChooseGame"), Filter = text.Get("ChooseFilter"), FileName = "LeagueClient.exe",
            InitialDirectory = preferences.GamePath ?? detectedGamePath ?? "", CheckFileExists = true
        };
        if (dialog.ShowDialog(window) != true) return;
        if (!String.Equals(Path.GetFileName(dialog.FileName), "LeagueClient.exe", StringComparison.OrdinalIgnoreCase))
        {
            installPath.Text = text.Get("ManualPathInvalid");
            return;
        }
        SelectGamePath(Path.GetDirectoryName(dialog.FileName));
    }

    private void SelectGamePath(string gamePath)
    {
        if (operation != null) return;
        preferences.GamePath = gamePath;
        SavePreferences();
        Initialize();
    }

    private void UpdateInstallPath()
    {
        bool manual = !String.IsNullOrWhiteSpace(preferences.GamePath);
        string path = manual ? preferences.GamePath : detectedGamePath;
        string display = text.Get(manual ? "PathModeManual" : "PathModeAuto", path ?? text.Get("PathUnknown"));
        installPath.Text = installProblem == null ? display : display + Environment.NewLine + installProblem.Render(text);
        manualPathButton.SetResourceReference(ContentControl.ContentProperty, manual ? "ChangePath" : "ChoosePath");
    }

    private void SavePreferences()
    {
        try
        {
            preferences.Save(preferencesPath);
            settingsFeedback.SetResourceReference(TextBlock.TextProperty, "PrefsSaved");
        }
        catch (IOException) { settingsFeedback.SetResourceReference(TextBlock.TextProperty, "PrefsFailure"); }
        catch (UnauthorizedAccessException) { settingsFeedback.SetResourceReference(TextBlock.TextProperty, "PrefsFailure"); }
    }

    private void SetTheme(ThemeMode theme)
    {
        preferences.Theme = theme;
        ApplyTheme();
        SavePreferences();
        AnimateEntrance(themeGlyph);
    }

    private void ApplyTheme()
    {
        darkTheme = WindowTheme.Apply(window, preferences.Theme);
        themeGlyph.SetResourceReference(System.Windows.Shapes.Path.DataProperty, darkTheme ? "SunIcon" : "MoonIcon");
        themeButton.SetResourceReference(AutomationProperties.NameProperty, darkTheme ? "ThemeLight" : "ThemeDark");
        themeButton.SetResourceReference(FrameworkElement.ToolTipProperty, darkTheme ? "ThemeLightHint" : "ThemeDarkHint");
        themeButton.IsEnabled = !SystemParameters.HighContrast;
    }

    private void AnimateEntrance(FrameworkElement element, double horizontalOffset = 0)
    {
        if (!window.IsLoaded || !SystemParameters.ClientAreaAnimation) return;
        element.BeginAnimation(UIElement.OpacityProperty, EntranceAnimation(0.6, 1));
        if (horizontalOffset != 0)
        {
            var translation = new TranslateTransform();
            element.RenderTransform = translation;
            translation.BeginAnimation(TranslateTransform.XProperty, EntranceAnimation(horizontalOffset, 0));
        }
    }

    private static DoubleAnimation EntranceAnimation(double from, double to)
    {
        return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(200)) {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
    }

    private void FitWindowToScreen()
    {
        var source = PresentationSource.FromVisual(window) as HwndSource;
        if (source == null || source.CompositionTarget == null) return;
        var area = System.Windows.Forms.Screen.FromHandle(source.Handle).WorkingArea;
        var logicalSize = source.CompositionTarget.TransformFromDevice.Transform(new Vector(area.Width, area.Height));
        double width = Math.Floor(logicalSize.X);
        double height = Math.Floor(logicalSize.Y);
        if (width <= 0 || height <= 0) return;

        window.MinWidth = Math.Min(minimumWindowSize.Width, width);
        window.MinHeight = Math.Min(minimumWindowSize.Height, height);
        window.MaxWidth = Math.Min(maximumWindowWidth, width);
        window.MaxHeight = height;
    }

    private void OnDisplaySettingsChanged(object sender, EventArgs args)
    {
        if (window.Dispatcher.HasShutdownStarted) return;
        window.Dispatcher.BeginInvoke(new Action(FitWindowToScreen));
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (window.Dispatcher.HasShutdownStarted) return;
        window.Dispatcher.BeginInvoke(new Action(ApplyTheme));
    }

    private void ShowSettings(bool show)
    {
        homePanel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        settingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        settingsButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        backButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        pageScroll.ScrollToTop();
        AnimateEntrance(show ? settingsPanel : homePanel, show ? 12 : -12);
        (show ? backButton : settingsButton).Focus();
    }

    public void Initialize(LeagueInstall installation = null)
    {
        string backup = Path.Combine(appDirectory, "original-locale.txt");
        detectedGamePath = null;
        installProblem = null;
        try
        {
            var found = installation ?? LeagueInstall.Find(preferences.GamePath);
            detectedGamePath = found.GameRoot;
            engine = new LeagueLauncher(found, backup);
            string current = engine.CurrentLocale;
            Show(new LaunchStatus(new UiMessage("ConfigCurrent", UiMessage.Locale(current)), current == LeagueLauncher.Chinese
                ? "ConfigChinese" : "ConfigOther"));
        }
        catch (Exception error)
        {
            engine = null;
            installProblem = UserMessage(error);
            Show(new LaunchStatus("InstallUnrecognized", installProblem));
        }
        UpdateInstallPath();
        Buttons();
    }

    private void Show(LaunchStatus status)
    {
        lastStatus = status;
        title.Text = status.Title.Render(text);
        AutomationProperties.SetName(title, title.Text);
        detail.Text = status.Detail.Render(text);
        bar.Visibility = status.Percent.HasValue ? Visibility.Visible : Visibility.Hidden;
        progressText.Visibility = bar.Visibility;
        if (status.Percent.HasValue)
        {
            bar.Value = status.Percent.Value;
            progressText.Text = String.Format(CultureInfo.InvariantCulture, "{0:0}%", Math.Floor(bar.Value));
        }
    }

    private void Buttons()
    {
        start.SetResourceReference(ContentControl.ContentProperty, operation != null ? "Stop" : engine == null ? "RetryInstall" : "Start");
        string original = operation == null && engine != null ? engine.OriginalLocale : null;
        restore.IsEnabled = original != null;
        restore.ToolTip = restore.IsEnabled
            ? text.Get("RestoreTooltip", UiMessage.Locale(original)) : text.Get("RestoreUnavailable");
        autoPathButton.IsEnabled = operation == null;
        manualPathButton.IsEnabled = operation == null;
    }

    private static UiMessage UserMessage(Exception error)
    {
        if (error is UnauthorizedAccessException)
            return "PermissionError";
        if (UiText.HasKey(error.Message)) return error.Message;
        return new UiMessage("UnknownError", error.Message);
    }

    private async void OnStart(object sender, RoutedEventArgs args)
    {
        if (operation != null) { operation.Cancel(); return; }
        if (engine == null) { Initialize(); if (engine == null) return; }
        await Run(false);
    }

    private async void OnRestore(object sender, RoutedEventArgs args)
    {
        if (operation == null && engine != null) await Run(true);
    }

    private async Task Run(bool restoring)
    {
        operation = new CancellationTokenSource();
        var cancel = operation;
        var progress = new Progress<LaunchStatus>(status => {
            if (operation == cancel && !cancel.IsCancellationRequested) Show(status);
        });
        Buttons();
        Show(new LaunchStatus(restoring ? "Restoring" : "Preparing", "CheckingProcesses"));
        try
        {
            LaunchStatus result = await Task.Run(() => restoring
                ? engine.Restore(progress, cancel.Token)
                : engine.SwitchToChinese(progress, cancel.Token), cancel.Token);
            Show(result);
        }
        catch (OperationCanceledException)
        {
            Show(new LaunchStatus("Stopped", "StoppedHint"));
        }
        catch (Exception error)
        {
            Show(new LaunchStatus("Incomplete", UserMessage(error)));
        }
        finally
        {
            operation = null;
            cancel.Dispose();
            Buttons();
        }
    }
}
