using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Rect = System.Windows.Rect;

internal static class RiotPlayButton
{
    internal const string WorkerArgument = "--riot-play-worker";
    private const int WorkerTimeoutMilliseconds = 4000;

    private static readonly Condition LeaguePageLink = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
        new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "leagueoflegends.com", PropertyConditionFlags.IgnoreCase),
            new PropertyCondition(AutomationElement.NameProperty, "www.leagueoflegends.com", PropertyConditionFlags.IgnoreCase)));

    private static readonly Condition LeagueHubMerchTab = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
        new PropertyCondition(AutomationElement.NameProperty, "Merch", PropertyConditionFlags.IgnoreCase));

    private static readonly Condition LeagueHubMacLink = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
        new PropertyCondition(AutomationElement.NameProperty, "MacOS", PropertyConditionFlags.IgnoreCase),
        new PropertyCondition(AutomationElement.ClassNameProperty, "button platform-button"));

    private static readonly Condition GameHubContentButton = new PropertyCondition(
        AutomationElement.ControlTypeProperty, ControlType.Button);

    private static readonly Regex LeagueHubContentName = new Regex(
        @"《英雄[聯联]盟》|\bLeague of Legends\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        return RunWorker(Assembly.GetEntryAssembly().Location, riotExe, cancel);
    }

    internal static bool RunWorker(string launcherExe, string riotExe, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(launcherExe, WorkerArgument) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true
        };
        using (Process worker = Process.Start(startInfo))
        {
            try
            {
                using (cancel.Register(() => StopWorker(worker)))
                {
                    cancel.ThrowIfCancellationRequested();
                    worker.StandardInput.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(riotExe)));
                    worker.StandardInput.Flush();
                    bool exited = worker.WaitForExit(WorkerTimeoutMilliseconds);
                    cancel.ThrowIfCancellationRequested();
                    return exited && worker.ExitCode == 0;
                }
            }
            catch (IOException)
            {
                cancel.ThrowIfCancellationRequested();
                return false;
            }
            finally { StopWorker(worker); }
        }
    }

    internal static int RunWorkerMode()
    {
        string request = Console.ReadLine();
        if (String.IsNullOrWhiteSpace(request)) return 1;
        string riotExe;
        try { riotExe = Encoding.UTF8.GetString(Convert.FromBase64String(request)); }
        catch (FormatException) { return 1; }

        // The owner keeps stdin open for the lifetime of the probe. EOF also
        // stops a blocked probe if the owner is closed outside its normal exit path.
        var watchOwner = new Thread(() => {
            try { Console.ReadLine(); }
            catch (IOException) { }
            using (Process self = Process.GetCurrentProcess()) StopWorker(self);
        }) { IsBackground = true };
        watchOwner.Start();

        // UI Automation providers can stall. Run the cross-process calls in
        // this disposable worker, on an MTA thread separate from the app window.
        return Task.Run(() => InvokeFromRiotWindow(riotExe)).GetAwaiter().GetResult() ? 0 : 1;
    }

    private static void StopWorker(Process worker)
    {
        try
        {
            if (worker.HasExited) return;
            worker.Kill();
            worker.WaitForExit(500);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static bool InvokeFromRiotWindow(string riotExe)
    {
        string riotDirectory = Path.GetDirectoryName(riotExe) + Path.DirectorySeparatorChar;
        foreach (string processName in new[] { "Riot Client", "RiotClientUx" })
        {
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        IntPtr handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero ||
                            !process.MainModule.FileName.StartsWith(riotDirectory, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var window = AutomationElement.FromHandle(handle);
                        if (TryInvokeOnLeaguePage(window)) return true;
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

    internal static bool TryInvokeOnLeaguePage(AutomationElement window)
    {
        InvokePattern play = FindLeaguePlay(window);
        if (play == null) return false;
        play.Invoke();
        return true;
    }

    internal static InvokePattern FindLeaguePlay(AutomationElement window)
    {
        if (window == null || window.Current.IsOffscreen) return null;

        bool hasWebsiteLink = window.FindFirst(TreeScope.Descendants, LeaguePageLink) != null;
        if (!hasWebsiteLink && !HasLeagueHubMarkers(window)) return null;

        Rect windowBounds = window.Current.BoundingRectangle;
        foreach (AutomationElement button in window.FindAll(TreeScope.Descendants, EnabledPlayButton))
        {
            if (!hasWebsiteLink && button.Current.ClassName != "cta-button-resting-state-button") continue;
            Rect bounds = button.Current.BoundingRectangle;
            object pattern;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ||
                !windowBounds.Contains(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2) ||
                !button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) continue;

            return (InvokePattern)pattern;
        }
        return null;
    }

    private static bool HasLeagueHubMarkers(AutomationElement window)
    {
        // The new League page may omit its website link. Require additional
        // product evidence because other Riot games use the same Play control.
        if (window.FindFirst(TreeScope.Descendants, LeagueHubMerchTab) == null ||
            window.FindFirst(TreeScope.Descendants, LeagueHubMacLink) == null) return false;

        foreach (AutomationElement button in window.FindAll(TreeScope.Descendants, GameHubContentButton))
        {
            var content = button.Current;
            if ((" " + content.ClassName + " ").Contains(" content-card-base ") &&
                LeagueHubContentName.IsMatch(content.Name ?? "")) return true;
        }
        return false;
    }
}
