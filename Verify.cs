using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Web.Script.Serialization;

internal static class Verify
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == RiotPlayButton.WorkerArgument)
        {
            using (var self = Process.GetCurrentProcess())
                File.WriteAllText(Encoding.UTF8.GetString(Convert.FromBase64String(Console.ReadLine())), self.Id.ToString());
            Thread.Sleep(Timeout.Infinite);
            return;
        }
        Program.ConfigureDpi();
        string root = args[0];
        Directory.CreateDirectory(root);
        VerifyWorkerCleanup(root, cancelEarly: true);
        VerifyWorkerCleanup(root, cancelEarly: false);
        Console.WriteLine("Blocked Play workers exit on cancellation and timeout.");
        string file = Path.Combine(root, "Riot fixture.yaml");
        string original = "locale_data:\r\n    default_locale: \"zh_TW\"\r\n" +
            "settings:\r\n    locale: \"zh_MY\" # 原语言\r\n    preserve: \"字\"\r\n" +
            "other:\r\n    locale: \"en_US\"\r\n";
        File.WriteAllText(file, original, new UTF8Encoding(true));
        byte[] snapshot = File.ReadAllBytes(file);
        var locale = new LocaleFile(file);
        Require(locale.Read() == "zh_MY");
        Require(locale.TryWrite("zh_CN") && locale.Read() == "zh_CN");
        string expected = original.Replace("zh_MY", "zh_CN");
        Require(File.ReadAllText(file) == expected);
        Require(locale.TryWrite("zh_TW", "en_US") && locale.Read() == "zh_CN");
        Require(locale.TryWrite("zh_MY") && File.ReadAllBytes(file).SequenceEqual(snapshot));
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Require(!locale.TryWrite("zh_CN"));
        File.WriteAllText(file, "settings:\n  locale: 'zh_TW'\n  other:\n    locale: en_US\n");
        Require(locale.TryWrite("zh_CN") && File.ReadAllText(file) ==
            "settings:\n  locale: 'zh_CN'\n  other:\n    locale: en_US\n");
        File.WriteAllText(file, "settings:\n  locale: zh_MY\n  locale: en_US\n");
        try { locale.Read(); throw new Exception("Duplicate language was accepted"); }
        catch (InvalidDataException) { }

        string fixture = Path.Combine(root, "模拟安装 路径");
        string common = Path.Combine(fixture, "ProgramData", "Riot Games");
        string game = Path.Combine(fixture, "游戏目录", "League of Legends");
        string riot = Path.Combine(fixture, "启动器 独立目录", "RiotClientServices.exe");
        string metadata = Path.Combine(common, "Metadata", "league_of_legends.live", "league_of_legends.live.product_settings.yaml");
        string registry = Path.Combine(common, "RiotClientInstalls.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadata));
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(Path.GetDirectoryName(riot));
        File.WriteAllText(Path.Combine(game, "LeagueClient.exe"), "");
        File.WriteAllText(riot, "");
        File.WriteAllText(metadata, "product_install_full_path: \"" + game.Replace('\\', '/') +
            "\"\nsettings:\n    locale: zh_TW\n");
        File.WriteAllText(registry, new JavaScriptSerializer().Serialize(new { rc_live = riot }));
        Require(LeagueInstall.Find(common, fixture).RiotExe == riot);
        File.Delete(registry);
        string sibling = Path.Combine(Path.GetDirectoryName(game), "Riot Client", "RiotClientServices.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(sibling));
        File.WriteAllText(sibling, "");
        var install = LeagueInstall.Find(common, fixture);
        Require(install.RiotExe == sibling);
        Require(LeagueInstall.Find(common, fixture, game).GameRoot == game);
        string otherGame = Path.Combine(fixture, "Other install");
        Directory.CreateDirectory(otherGame);
        File.WriteAllText(Path.Combine(otherGame, "LeagueClient.exe"), "");
        try { LeagueInstall.Find(common, fixture, otherGame); throw new Exception("Conflicting Riot installation was accepted"); }
        catch (InvalidDataException error) { Require(error.Message == "ErrorPathConflict"); }
        string registeredSettings = File.ReadAllText(metadata);
        File.WriteAllText(metadata, "settings:\n    locale: zh_TW\n");
        Require(LeagueInstall.Find(common, fixture, game).GameRoot == game);
        File.WriteAllText(metadata, registeredSettings);
        Console.WriteLine("Locale changes, restore, file locks, duplicate rejection, and install discovery passed.");

        string appData = Path.Combine(root, "preferences");
        Directory.CreateDirectory(appData);
        string preferenceFile = Path.Combine(appData, "preferences.json");
        File.WriteAllText(preferenceFile, "{\"Theme\":2,\"UiLanguage\":\"en-US\",\"OpenGameAutomatically\":true,\"WaitMinutes\":45}");
        var legacy = LauncherPreferences.Load(preferenceFile);
        Require(legacy.Theme == ThemeMode.Dark && legacy.UiLanguage == "en-US");
        legacy.Save(preferenceFile);
        string migrated = File.ReadAllText(preferenceFile);
        Require(!migrated.Contains("WaitMinutes") && !migrated.Contains("OpenGameAutomatically"));
        File.WriteAllText(preferenceFile, "broken json");
        LauncherPreferences defaults = LauncherPreferences.Load(preferenceFile);
        Require(defaults.Theme == ThemeMode.System
            && defaults.UiLanguage == "zh-CN" && defaults.GamePath == null);
        defaults.Theme = ThemeMode.Light;
        defaults.Save(preferenceFile);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        VerifyAutoPlay();
        Window window = Program.LoadWindow();
        Require(window.Width == 420 && window.Width == window.MinWidth
            && window.Height == 380 && window.Height == window.MinHeight);
        new LauncherWindow(window, appData).Initialize(install);
        var handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        IntPtr dpiAwareness = GetWindowDpiAwarenessContext(handle);
        Require(AreDpiAwarenessContextsEqual(dpiAwareness, new IntPtr(-4))
            || AreDpiAwarenessContextsEqual(dpiAwareness, new IntPtr(-3)));
        var language = (Button)window.FindName("LanguageButton");
        var manualPath = (Button)window.FindName("ManualPathButton");
        var autoPathItem = (MenuItem)window.FindName("AutoPathItem");
        var start = (Button)window.FindName("StartButton");
        var theme = (Button)window.FindName("ThemeButton");
        var scroll = (ScrollViewer)window.FindName("PageScroll");
        Require(!autoPathItem.IsEnabled && AutomationProperties.GetName(manualPath) == "手动选择");
        Require(AutomationProperties.GetName(theme) == "切换为深色模式");
        Draw(window, 404, 341, Path.Combine(root, "settings-light.png"));
        theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        language.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(AutomationProperties.GetName(language) == "Switch interface to Simplified Chinese"
            && AutomationProperties.GetName(theme) == "Switch to light theme");
        Require(((TextBlock)window.FindName("InstallPath")).Text.StartsWith("Auto-detect"));
        Require(((TextBlock)window.FindName("StateTitle")).Text == "Current setting: Chinese (Taiwan)");
        LauncherPreferences saved = LauncherPreferences.Load(preferenceFile);
        Require(saved.Theme == ThemeMode.Dark && saved.UiLanguage == "en-US");
        ((TextBlock)window.FindName("InstallPath")).Text = @"Auto-detect · D:\Riot Games\League of Legends";
        Draw(window, 404, 341, Path.Combine(root, "settings-dark.png"));
        Draw(window, 340, 285, Path.Combine(root, "settings-compact.png"), 144);
        Draw(window, 404, 341, Path.Combine(root, "home-dark.png"), 144);
        Require(scroll.ScrollableHeight == 0);
        language.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(AutomationProperties.GetName(language) == "切换为英文界面"
            && LauncherPreferences.Load(preferenceFile).UiLanguage == "zh-CN");
        ((TextBlock)window.FindName("InstallPath")).Text = @"自动识别 · D:\Riot Games\League of Legends";
        Draw(window, 404, 341, Path.Combine(root, "settings-dark-125.png"), 120);
        var pathButtons = (Grid)manualPath.Parent;
        double iconCenter = manualPath.TranslatePoint(new Point(0, manualPath.ActualHeight / 2), pathButtons).Y;
        Require(Math.Abs(iconCenter - pathButtons.ActualHeight / 2) <= 1);
        var focusCue = new Control {
            Style = manualPath.FocusVisualStyle, IsHitTestVisible = false, Margin = manualPath.Margin,
            Height = manualPath.MinHeight, VerticalAlignment = manualPath.VerticalAlignment
        };
        Grid.SetColumn(focusCue, Grid.GetColumn(manualPath));
        pathButtons.Children.Add(focusCue);
        Draw(window, 404, 341, Path.Combine(root, "settings-compact-150.png"), 144);
        pathButtons.Children.Remove(focusCue);
        ((TextBlock)window.FindName("StateTitle")).Text = "Riot 正在下载或安装语言资源";
        ((TextBlock)window.FindName("StateDetail")).Text = "下载完成后，工具会自动启动 LOL。";
        var progress = (ProgressBar)window.FindName("Progress");
        ((FrameworkElement)window.FindName("ProgressRow")).Visibility = Visibility.Visible;
        progress.Value = 63;
        var percent = (TextBlock)window.FindName("ProgressText");
        percent.Text = "63%";
        start.Content = "停止等待";
        Draw(window, 404, 341, Path.Combine(root, "progress-compact.png"), 192);
        Draw(window, 344, 215, Path.Combine(root, "progress-small-screen.png"), 192);
        var view = (FrameworkElement)window.Content;
        Point startPosition = start.TranslatePoint(new Point(), view);
        scroll.ScrollToEnd();
        Draw(window, 344, 215, Path.Combine(root, "progress-small-screen-scrolled.png"), 192);
        Require(scroll.VerticalOffset > 0 && start.TranslatePoint(new Point(), view) == startPosition);
        saved.GamePath = game;
        saved.Save(preferenceFile);
        Window reopened = Program.LoadWindow();
        new LauncherWindow(reopened, appData).Initialize(install);
        Require(AutomationProperties.GetName((Button)reopened.FindName("ThemeButton")) == "Switch to light theme");
        Require(AutomationProperties.GetName((Button)reopened.FindName("LanguageButton")) == "Switch interface to Simplified Chinese");
        Require(((TextBlock)reopened.FindName("InstallPath")).Text == "Manual · " + game);
        Require(((MenuItem)reopened.FindName("AutoPathItem")).IsEnabled
            && AutomationProperties.GetName((Button)reopened.FindName("ManualPathButton")) == "Change path");
        var systemThemeItem = (MenuItem)window.FindName("SystemThemeItem");
        systemThemeItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(LauncherPreferences.Load(preferenceFile).Theme == ThemeMode.System);
        window.Close();
        Console.WriteLine("WPF pages rendered, and settings persist on reopening.");
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("A launcher verification failed.");
    }

    private static void VerifyWorkerCleanup(string root, bool cancelEarly)
    {
        string marker = Path.Combine(root, "worker-" + Guid.NewGuid().ToString("N") + ".pid");
        using (var cancel = new CancellationTokenSource())
        {
            var invocation = Task.Run(() => RiotPlayButton.RunWorker(Assembly.GetExecutingAssembly().Location, marker, cancel.Token));
            try
            {
                Require(SpinWait.SpinUntil(() => File.Exists(marker) && new FileInfo(marker).Length > 0, 2500));
                using (var worker = Process.GetProcessById(Int32.Parse(File.ReadAllText(marker))))
                {
                    if (cancelEarly)
                    {
                        cancel.Cancel();
                        try { invocation.GetAwaiter().GetResult(); throw new InvalidOperationException("Expected cancellation."); }
                        catch (OperationCanceledException) { }
                    }
                    else Require(!invocation.GetAwaiter().GetResult());
                    Require(worker.WaitForExit(1000));
                }
            }
            finally
            {
                cancel.Cancel();
                File.Delete(marker);
            }
        }
    }

    private static void VerifyAutoPlay()
    {
        var page = new StackPanel();
        var link = new Hyperlink(new Run("example.com"));
        page.Children.Add(new TextBlock(link));
        var disabled = new Button { Content = "遊玩", IsEnabled = false, Height = 28 };
        var hidden = new Button { Content = "遊玩", Visibility = Visibility.Collapsed, Height = 28 };
        var play = new Button { Content = "遊玩", Height = 28 };
        int clicks = 0;
        play.Click += (sender, args) => clicks++;
        disabled.Click += (sender, args) => { throw new InvalidOperationException("Disabled Play was invoked."); };
        hidden.Click += (sender, args) => { throw new InvalidOperationException("Hidden Play was invoked."); };
        page.Children.Add(disabled);
        page.Children.Add(hidden);
        page.Children.Add(play);

        var fixture = new Window {
            Width = 320, Height = 240, Left = -20000, Top = -20000,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Opacity = 0,
            ShowInTaskbar = false, ShowActivated = false, Content = page
        };
        fixture.Show();
        try
        {
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(fixture).Handle;
            Require(!InvokeFixturePlay(handle) && clicks == 0);
            AutomationProperties.SetName(link, "leagueoflegends.com");
            Require(InvokeFixturePlay(handle));
            fixture.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            Require(clicks == 1);
            play.IsEnabled = false;
            Require(!InvokeFixturePlay(handle) && clicks == 1);
            Console.WriteLine("UI Automation verified the League page and invoked only enabled, visible Play.");
        }
        finally { fixture.Close(); }
    }

    private static bool InvokeFixturePlay(IntPtr handle)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var invocation = Task.Run(() => RiotPlayButton.TryInvokeOnLeaguePage(AutomationElement.FromHandle(handle)));
        invocation.ContinueWith(task => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
        Dispatcher.PushFrame(frame);
        return invocation.GetAwaiter().GetResult();
    }
    private static void Draw(Window window, int width, int height, string file, int dpi = 96)
    {
        var view = (FrameworkElement)window.Content;
        double scale = dpi / 96.0;
        VisualTreeHelper.SetRootDpi(window, new DpiScale(scale, scale));
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
            dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(file)) encoder.Save(stream);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr left, IntPtr right);
}
