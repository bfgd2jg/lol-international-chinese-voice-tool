using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

[assembly: AssemblyTitle("英雄联盟大陆简中启动器")]
[assembly: AssemblyDescription("通过 Riot 启动器切换英雄联盟国际服的语言资源")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

internal static class Program
{
    [STAThread]
    public static void Main()
    {
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
    private UiText text;
    private LaunchStatus lastStatus;
    private UiMessage installProblem;
    private string detectedGamePath;
    private LeagueLauncher engine;
    private CancellationTokenSource operation;

    public LauncherWindow(Window view, string appDataDirectory = null)
    {
        window = view;
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
        homePanel = (FrameworkElement)view.FindName("HomePanel");
        settingsPanel = (FrameworkElement)view.FindName("SettingsPanel");
        pageScroll = (ScrollViewer)view.FindName("PageScroll");
        settingsFeedback = (TextBlock)view.FindName("SettingsFeedback");
        autoPathButton = (Button)view.FindName("AutoPathButton");
        manualPathButton = (Button)view.FindName("ManualPathButton");
        installPath = (TextBlock)view.FindName("InstallPath");
        start.Click += OnStart;
        restore.Click += OnRestore;
        settingsButton.Click += (sender, args) => ShowSettings(settingsPanel.Visibility != Visibility.Visible);
        autoPathButton.Click += (sender, args) => SelectGamePath(null);
        manualPathButton.Click += ChooseGamePath;
        ConfigureSettings();
        ApplyUiLanguage();
        ApplyTheme();
        window.SourceInitialized += (sender, args) => {
            ApplyTheme();
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        };
        window.Activated += (sender, args) => { if (preferences.Theme == ThemeMode.System) ApplyTheme(); };
        window.Closing += (sender, args) => { if (operation != null) operation.Cancel(); };
        window.Closed += (sender, args) => { SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged; };
    }

    private void ConfigureSettings()
    {
        var chinese = (RadioButton)window.FindName("UiChinese");
        var english = (RadioButton)window.FindName("UiEnglish");
        chinese.IsChecked = preferences.UiLanguage == "zh-CN";
        english.IsChecked = preferences.UiLanguage == "en-US";
        chinese.Checked += (sender, args) => SetUiLanguage("zh-CN");
        english.Checked += (sender, args) => SetUiLanguage("en-US");
        foreach (string name in new[] { "ThemeSystem", "ThemeLight", "ThemeDark" })
        {
            var choice = (RadioButton)window.FindName(name);
            var mode = (ThemeMode)Enum.Parse(typeof(ThemeMode), (string)choice.Tag);
            choice.IsChecked = preferences.Theme == mode;
            choice.Checked += (sender, args) => {
                preferences.Theme = mode;
                ApplyTheme();
                SavePreferences();
            };
        }
        var automatic = (RadioButton)window.FindName("LaunchAuto");
        var manual = (RadioButton)window.FindName("LaunchManual");
        automatic.IsChecked = preferences.OpenGameAutomatically;
        manual.IsChecked = !preferences.OpenGameAutomatically;
        automatic.Checked += (sender, args) => SetAutoOpen(true);
        manual.Checked += (sender, args) => SetAutoOpen(false);
        foreach (string name in new[] { "Wait20", "Wait45", "Wait90" })
        {
            var choice = (RadioButton)window.FindName(name);
            int minutes = Int32.Parse((string)choice.Tag, CultureInfo.InvariantCulture);
            choice.IsChecked = preferences.WaitMinutes == minutes;
            choice.Checked += (sender, args) => {
                preferences.WaitMinutes = minutes;
                SavePreferences();
            };
        }
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

    private void SetAutoOpen(bool enabled)
    {
        preferences.OpenGameAutomatically = enabled;
        SavePreferences();
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

    private void ApplyTheme() { WindowTheme.Apply(window, preferences.Theme); }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (window.Dispatcher.HasShutdownStarted) return;
        window.Dispatcher.BeginInvoke(new Action(ApplyTheme));
    }

    private void ShowSettings(bool show)
    {
        homePanel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        settingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        settingsButton.SetResourceReference(ContentControl.ContentProperty, show ? "Back" : "Settings");
        pageScroll.ScrollToTop();
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
        bool autoOpen = preferences.OpenGameAutomatically;
        var wait = TimeSpan.FromMinutes(preferences.WaitMinutes);
        try
        {
            LaunchStatus result = await Task.Run(() => restoring
                ? engine.Restore(autoOpen, wait, progress, cancel.Token)
                : engine.SwitchAndLaunch(autoOpen, wait, progress, cancel.Token), cancel.Token);
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
