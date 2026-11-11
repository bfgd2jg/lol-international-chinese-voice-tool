using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

internal static class WindowTheme
{
    private static readonly string[] Keys = {
        "Canvas", "Panel", "Ink", "Muted", "Border",
        "Accent", "AccentInk", "Focus", "Track", "DisabledInk"
    };

    private static readonly string[] Light = {
        "#F2F5FA", "#FFFFFF", "#142238", "#445772", "#8295B0",
        "#1F55CC", "#FFFFFF", "#164BB4", "#D8E2F0", "#596A81"
    };

    private static readonly string[] Dark = {
        "#101722", "#182232", "#F3F6FC", "#B7C6DB", "#617994",
        "#8AB0FF", "#101D36", "#9BBCFF", "#34455C", "#96AAC3"
    };

    public static bool Apply(Window window, ThemeMode mode)
    {
        bool highContrast = SystemParameters.HighContrast;
        bool dark = !highContrast && (mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemDark()));
        string[] palette = dark ? Dark : Light;
        Brush[] system = highContrast ? new Brush[] {
            SystemColors.WindowBrush, SystemColors.WindowBrush, SystemColors.WindowTextBrush, SystemColors.WindowTextBrush,
            SystemColors.WindowTextBrush,
            SystemColors.HighlightBrush, SystemColors.HighlightTextBrush,
            SystemColors.WindowTextBrush, SystemColors.ControlBrush, SystemColors.GrayTextBrush
        } : null;
        for (int i = 0; i < Keys.Length; i++)
        {
            if (system != null) window.Resources[Keys[i]] = system[i];
            else
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
                brush.Freeze();
                window.Resources[Keys[i]] = brush;
            }
        }
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            int useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref useDark, sizeof(int));
        }
        return dark;
    }

    private static bool SystemDark()
    {
        try
        {
            object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int && (int)value == 0;
        }
        catch (SecurityException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
