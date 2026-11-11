using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

internal enum ThemeMode { System, Light, Dark }

internal sealed class LauncherPreferences
{
    public ThemeMode Theme { get; set; }
    public string UiLanguage { get; set; }
    public string GamePath { get; set; }
    public bool OpenGameAutomatically { get; set; }

    public LauncherPreferences()
    {
        Theme = ThemeMode.System;
        UiLanguage = "zh-CN";
        OpenGameAutomatically = true;
    }

    public static string AppDirectory { get { return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeagueChineseLocaleLauncher"); } }

    internal static LauncherPreferences Load(string path)
    {
        try
        {
            var settings = new JavaScriptSerializer().Deserialize<LauncherPreferences>(File.ReadAllText(path));
            if (settings != null)
            {
                if (!Enum.IsDefined(typeof(ThemeMode), settings.Theme)) settings.Theme = ThemeMode.System;
                if (settings.UiLanguage != "en-US") settings.UiLanguage = "zh-CN";
                return settings;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        return new LauncherPreferences();
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string pending = path + ".tmp";
        File.WriteAllText(pending, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(pending, path, null);
        else File.Move(pending, path);
    }
}
