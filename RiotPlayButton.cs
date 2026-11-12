using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using Rect = System.Windows.Rect;

internal static class RiotPlayButton
{
    private static readonly Condition LeaguePageLink = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
        new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "leagueoflegends.com", PropertyConditionFlags.IgnoreCase),
            new PropertyCondition(AutomationElement.NameProperty, "www.leagueoflegends.com", PropertyConditionFlags.IgnoreCase)));

    private static readonly Condition EnabledPlayButton = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new PropertyCondition(AutomationElement.IsEnabledProperty, true),
        new PropertyCondition(AutomationElement.IsOffscreenProperty, false),
        new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "游玩"),
            new PropertyCondition(AutomationElement.NameProperty, "遊玩"),
            new PropertyCondition(AutomationElement.NameProperty, "Play", PropertyConditionFlags.IgnoreCase)));

    public static bool TryInvoke(string riotExe, CancellationToken cancel)
    {
        string riotDirectory = Path.GetDirectoryName(riotExe) + Path.DirectorySeparatorChar;
        foreach (string processName in new[] { "Riot Client", "RiotClientUx" })
        {
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                foreach (Process process in processes)
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        IntPtr handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero ||
                            !process.MainModule.FileName.StartsWith(riotDirectory, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var window = AutomationElement.FromHandle(handle);
                        if (TryInvokeOnLeaguePage(window, cancel)) return true;
                    }
                    catch (ElementNotAvailableException) { }
                    catch (ElementNotEnabledException) { }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    catch (COMException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }
        return false;
    }

    internal static bool TryInvokeOnLeaguePage(AutomationElement window, CancellationToken cancel)
    {
        if (window == null || window.Current.IsOffscreen ||
            window.FindFirst(TreeScope.Descendants, LeaguePageLink) == null) return false;

        Rect windowBounds = window.Current.BoundingRectangle;
        foreach (AutomationElement button in window.FindAll(TreeScope.Descendants, EnabledPlayButton))
        {
            Rect bounds = button.Current.BoundingRectangle;
            object pattern;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ||
                !windowBounds.Contains(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2) ||
                !button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) continue;

            cancel.ThrowIfCancellationRequested();
            ((InvokePattern)pattern).Invoke();
            return true;
        }
        return false;
    }
}
