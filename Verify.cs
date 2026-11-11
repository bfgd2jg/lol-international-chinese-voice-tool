using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Web.Script.Serialization;

internal static class Verify
{
    [STAThread]
    public static void Main(string[] args)
    {
        string root = args[0];
        Directory.CreateDirectory(root);
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
        File.WriteAllText(preferenceFile, "broken json");
        LauncherPreferences defaults = LauncherPreferences.Load(preferenceFile);
        Require(defaults.Theme == ThemeMode.System && defaults.OpenGameAutomatically && defaults.WaitMinutes == 20
            && defaults.UiLanguage == "zh-CN" && defaults.GamePath == null);
        var app = new Application();
        Window window = Program.LoadWindow();
        new LauncherWindow(window, appData).Initialize(install);
        new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        var settings = (Button)window.FindName("SettingsButton");
        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(((FrameworkElement)window.FindName("SettingsPanel")).Visibility == Visibility.Visible);
        ((RadioButton)window.FindName("ThemeLight")).IsChecked = true;
        Draw(window, 604, 663, Path.Combine(root, "settings-light.png"));
        ((RadioButton)window.FindName("ThemeDark")).IsChecked = true;
        ((RadioButton)window.FindName("LaunchManual")).IsChecked = true;
        ((RadioButton)window.FindName("Wait45")).IsChecked = true;
        ((RadioButton)window.FindName("UiEnglish")).IsChecked = true;
        Require((string)settings.Content == "Back");
        Require(((TextBlock)window.FindName("InstallPath")).Text.StartsWith("Auto-detect"));
        Require(((TextBlock)window.FindName("StateTitle")).Text == "Current setting: Chinese (Taiwan)");
        LauncherPreferences saved = LauncherPreferences.Load(preferenceFile);
        Require(saved.Theme == ThemeMode.Dark && !saved.OpenGameAutomatically && saved.WaitMinutes == 45 && saved.UiLanguage == "en-US");
        ((TextBlock)window.FindName("InstallPath")).Text = @"Auto-detect · D:\Riot Games\League of Legends";
        Draw(window, 604, 703, Path.Combine(root, "settings-dark.png"));
        Draw(window, 404, 431, Path.Combine(root, "settings-compact.png"));
        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(((FrameworkElement)window.FindName("HomePanel")).Visibility == Visibility.Visible);
        Require((string)settings.Content == "Settings");
        Draw(window, 604, 531, Path.Combine(root, "home-dark.png"));
        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((RadioButton)window.FindName("UiChinese")).IsChecked = true;
        Require((string)settings.Content == "返回");
        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((TextBlock)window.FindName("StateTitle")).Text = "Riot 正在下载或安装语言资源";
        ((TextBlock)window.FindName("StateDetail")).Text = "保持启动器打开。下载完成后，在 Riot 客户端点「游玩」。";
        var progress = (ProgressBar)window.FindName("Progress");
        progress.Visibility = Visibility.Visible;
        progress.Value = 63;
        var percent = (TextBlock)window.FindName("ProgressText");
        percent.Text = "63%";
        percent.Visibility = Visibility.Visible;
        ((Button)window.FindName("StartButton")).Content = "停止等待";
        Draw(window, 404, 431, Path.Combine(root, "progress-compact.png"));
        saved.GamePath = game;
        saved.Save(preferenceFile);
        Window reopened = Program.LoadWindow();
        new LauncherWindow(reopened, appData).Initialize(install);
        Require(((RadioButton)reopened.FindName("ThemeDark")).IsChecked == true);
        Require(((RadioButton)reopened.FindName("LaunchManual")).IsChecked == true);
        Require(((RadioButton)reopened.FindName("Wait45")).IsChecked == true);
        Require(((RadioButton)reopened.FindName("UiEnglish")).IsChecked == true);
        Require(((TextBlock)reopened.FindName("InstallPath")).Text == "Manual · " + game);
        window.Close();
        Console.WriteLine("WPF pages rendered, and settings persist on reopening.");
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("A launcher verification failed.");
    }
    private static void Draw(Window window, int width, int height, string file)
    {
        var view = (ScrollViewer)window.Content;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(file)) encoder.Save(stream);
    }
}
