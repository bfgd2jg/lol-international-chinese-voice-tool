using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class LeagueInstall
{
    public string GameRoot;
    public string RiotExe;
    public string Metadata;
    public string RiotLogs;

    public static LeagueInstall Find(string manualPath = null)
    {
        return Find(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), manualPath);
    }

    internal static LeagueInstall Find(string common, string localAppData, string manualPath = null)
    {
        string metadata = Path.Combine(common, "Metadata", "league_of_legends.live", "league_of_legends.live.product_settings.yaml");
        if (!File.Exists(metadata)) throw new FileNotFoundException("ErrorMetadataMissing");
        Match match = Regex.Match(File.ReadAllText(metadata), @"(?m)^product_install_full_path:[ \t]*(?<path>[^\r\n]+)");
        string registered = match.Success ? match.Groups["path"].Value.Trim().Trim('"', '\'') : null;
        string root = GameDirectory(registered);
        if (!String.IsNullOrWhiteSpace(manualPath))
        {
            string manual = GameDirectory(manualPath);
            if (manual == null) throw new FileNotFoundException("ErrorGameMissing");
            if (root != null && !String.Equals(root, manual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ErrorPathConflict");
            root = manual;
        }
        if (root == null) throw new FileNotFoundException(match.Success ? "ErrorGameMissing" : "ErrorInstallPath");
        var candidates = new List<string>();
        try
        {
            string registry = Path.Combine(common, "RiotClientInstalls.json");
            var installs = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(registry));
            foreach (string key in new[] { "rc_live", "rc_default" })
            {
                object value;
                if (installs.TryGetValue(key, out value) && value is string) candidates.Add((string)value);
            }
        }
        catch (IOException) { }
        catch (ArgumentException) { }
        string parent = Path.GetDirectoryName(root);
        if (!String.IsNullOrEmpty(parent)) candidates.Add(Path.Combine(parent, "Riot Client", "RiotClientServices.exe"));
        string riot = candidates.FirstOrDefault(p => !String.IsNullOrWhiteSpace(p) && Path.IsPathRooted(p) &&
            String.Equals(Path.GetFileName(p), "RiotClientServices.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p));
        if (riot == null) throw new FileNotFoundException("ErrorRiotMissing");
        return new LeagueInstall {
            GameRoot = root, RiotExe = Path.GetFullPath(riot), Metadata = metadata,
            RiotLogs = Path.Combine(localAppData, "Riot Games", "Riot Client", "Logs")
        };
    }

    private static string GameDirectory(string path)
    {
        try
        {
            if (!Path.IsPathRooted(path)) return null;
            string fullPath = Path.GetFullPath(path);
            if (fullPath.Length > Path.GetPathRoot(fullPath).Length)
                fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return File.Exists(Path.Combine(fullPath, "LeagueClient.exe")) ? fullPath : null;
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }
}

internal sealed class LaunchStatus
{
    public readonly UiMessage Title;
    public readonly UiMessage Detail;
    public readonly double? Percent;
    public LaunchStatus(UiMessage title, UiMessage detail, double? percent = null)
    { Title = title; Detail = detail; Percent = percent; }
}

internal sealed class LeagueLauncher
{
    public const string Chinese = "zh_CN";
    private readonly LeagueInstall install;
    private readonly LocaleFile locale;
    private readonly string backup;
    private static readonly Regex LaunchLocale = new Regex(@"--locale=([a-z]{2}_[A-Z]{2})\b");
    private static readonly Regex LaunchRegion = new Regex(@"--region=([A-Z0-9]+)\b");
    private static readonly Regex PatchStatus = new Regex(
        @"productId: league_of_legends, patchlineId: live, status: ([A-Za-z]+), progress: ([0-9.]+)");

    public LeagueLauncher(LeagueInstall found, string backupPath)
    {
        install = found;
        locale = new LocaleFile(install.Metadata);
        backup = backupPath;
    }
    public string CurrentLocale { get { return locale.Read(); } }
    public string OriginalLocale
    {
        get {
            try {
                if (!File.Exists(backup)) return null;
                string value = File.ReadAllText(backup).Trim();
                return Regex.IsMatch(value, @"\A[a-z]{2}_[A-Z]{2}\z") ? value : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }

    private static bool IsRunning(params string[] names)
    {
        foreach (string name in names)
        {
            Process[] processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) return true; }
            finally { foreach (Process process in processes) process.Dispose(); }
        }
        return false;
    }
    private static bool GameRunning() { return IsRunning("League of Legends", "TFTClient-Win64-Shipping"); }
    private static bool ClientRunning() { return IsRunning("LeagueClient", "LeagueClientUx"); }

    private static string LogText(string path, int max, bool tail)
    {
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (tail && file.Length > max) file.Seek(-max, SeekOrigin.End);
            var bytes = new byte[Math.Min(max, (int)Math.Min(file.Length, Int32.MaxValue))];
            int count = file.Read(bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes, 0, count);
        }
    }
    private Tuple<string, string> RunningLocale()
    {
        try
        {
            string lockPath = Path.Combine(install.GameRoot, "lockfile");
            string[] fields = LogText(lockPath, 4096, false).Split(':');
            int pid;
            if (fields.Length < 2 || !Int32.TryParse(fields[1], out pid)) return null;
            using (var process = Process.GetProcessById(pid))
                if (!String.Equals(process.ProcessName, "LeagueClient", StringComparison.OrdinalIgnoreCase)) return null;
            string logs = Path.Combine(install.GameRoot, "Logs", "LeagueClient Logs");
            FileInfo match = new DirectoryInfo(logs).GetFiles("*_" + pid + "_LeagueClient.log")
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (match == null || DateTime.UtcNow - match.LastWriteTimeUtc > TimeSpan.FromHours(24)) return null;
            string head = LogText(match.FullName, 16000, false);
            Match language = LaunchLocale.Match(head);
            return language.Success ? Tuple.Create(language.Groups[1].Value, LaunchRegion.Match(head).Groups[1].Value) : null;
        }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private Match ReadPatchStatus(DateTime since)
    {
        try
        {
            foreach (var log in new DirectoryInfo(install.RiotLogs).EnumerateFiles("*_Riot Client.log", SearchOption.AllDirectories)
                .Where(f => f.LastWriteTimeUtc >= since).OrderByDescending(f => f.LastWriteTimeUtc).Take(4))
            {
                try {
                    var matches = PatchStatus.Matches(LogText(log.FullName, 96000, true));
                    if (matches.Count != 0) return matches[matches.Count - 1];
                } catch (IOException) { }
            }
        }
        catch (IOException) { }
        return null;
    }
    private bool HasLanguageResources()
    {
        return File.Exists(Path.Combine(install.GameRoot, "Plugins", "rcp-be-lol-game-data", "zh_CN-assets.wad"))
            && File.Exists(Path.Combine(install.GameRoot, "Game", "DATA", "FINAL", "Localized", "Global.zh_CN.wad.client"))
            && File.Exists(Path.Combine(install.GameRoot, "Game", "DATA", "FINAL", "Champions", "Aatrox.zh_CN.wad.client"));
    }
    private void OpenRiot()
    {
        var info = new ProcessStartInfo(install.RiotExe) {
            UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(install.RiotExe)
        };
        using (Process launcher = Process.Start(info)) { }
    }
    private void Remember(string code)
    {
        if (code == Chinese || File.Exists(backup)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        using (var file = new FileStream(backup, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(code);
    }
    private async Task SetLocale(string code, CancellationToken cancel)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            cancel.ThrowIfCancellationRequested();
            if (locale.TryWrite(code)) return;
            await Task.Delay(250, cancel).ConfigureAwait(false);
        }
        throw new IOException("ErrorConfigBusy");
    }
    private LaunchStatus Ready(Tuple<string, string> running, string target)
    {
        bool chinese = target == Chinese;
        bool files = chinese && HasLanguageResources();
        UiMessage detail = chinese ? (files ? "AssetsFound" : "AssetsUncertain") : "LocaleVerified";
        if (!String.IsNullOrEmpty(running.Item2)) detail = new UiMessage("Region", running.Item2, detail);
        UiMessage title = chinese ? (UiMessage)"ChineseStarted" : new UiMessage("Restored", UiMessage.Locale(target));
        return new LaunchStatus(title, detail, files ? 100 : (double?)null);
    }

    public Task<LaunchStatus> SwitchToChinese(IProgress<LaunchStatus> progress, CancellationToken cancel)
    {
        return UseLanguage(Chinese, progress, cancel);
    }

    public async Task<LaunchStatus> Restore(IProgress<LaunchStatus> progress, CancellationToken cancel)
    {
        string original = OriginalLocale;
        if (original == null) return new LaunchStatus("NoBackup", "RestoreUnavailable");
        return await UseLanguage(original, progress, cancel).ConfigureAwait(false);
    }

    private async Task<LaunchStatus> UseLanguage(string target,
        IProgress<LaunchStatus> progress, CancellationToken cancel)
    {
        if (GameRunning()) return new LaunchStatus("InGame", "InGameHint");
        if (ClientRunning())
        {
            var current = RunningLocale();
            if (current != null && current.Item1 == target) return Ready(current, target);
            return new LaunchStatus("LobbyOpen", "LobbyHint");
        }
        string previous = locale.Read();
        Match currentPatch = ReadPatchStatus(DateTime.UtcNow.AddSeconds(-10));
        if (previous != target && currentPatch != null && currentPatch.Groups[1].Value == "Updating")
            return new LaunchStatus("RiotUpdating", "RiotUpdatingHint");
        if (target == Chinese) Remember(previous);
        await SetLocale(target, cancel).ConfigureAwait(false);
        DateTime started = DateTime.UtcNow;
        DateTime nextStatus = started;
        bool sawDownload = false;
        DateTime? riotMissingSince = null;
        OpenRiot();
        progress.Report(new LaunchStatus("RiotStarting", "RiotStartingManual"));

        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            if (GameRunning())
                return new LaunchStatus("MatchStarted", "MatchStartedHint");
            var running = RunningLocale();
            if (running != null)
            {
                if (running.Item1 == target) return Ready(running, target);
                return new LaunchStatus(new UiMessage("WrongLocale", UiMessage.Locale(running.Item1)), "WrongLocaleHint");
            }
            locale.TryWrite(target);
            DateTime now = DateTime.UtcNow;
            if (now >= nextStatus)
            {
                bool riotRunning = IsRunning("RiotClientServices", "RiotClientUx", "RiotClientUxRender", "Riot Client");
                if (!riotRunning && !ClientRunning())
                {
                    if (!riotMissingSince.HasValue) riotMissingSince = now;
                    if (now - riotMissingSince.Value >= TimeSpan.FromSeconds(10))
                        return new LaunchStatus("RiotClosed", "RiotClosedHint");
                }
                else riotMissingSince = null;
                Match patch = ReadPatchStatus(started.AddSeconds(-2));
                if (patch != null && patch.Groups[1].Value == "Updating")
                {
                    double percent;
                    bool found = Double.TryParse(patch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
                    sawDownload = true;
                    progress.Report(new LaunchStatus("Downloading",
                        "DownloadingHint",
                        found ? Math.Min(99, percent) : (double?)null));
                }
                else if (riotRunning && (target != Chinese || HasLanguageResources()) &&
                    ((patch != null && patch.Groups[1].Value == "UpToDate") || (patch == null && !sawDownload)))
                    progress.Report(new LaunchStatus("AssetsReady", "AssetsReadyHint", 100));
                else if (riotRunning && now - started > TimeSpan.FromSeconds(50))
                {
                    progress.Report(new LaunchStatus("WaitUpdate",
                        "WaitUpdateHint"));
                }
                else if (riotRunning)
                    progress.Report(new LaunchStatus("WaitRiot", "WaitRiotHint"));
                nextStatus = now.AddSeconds(2);
            }
            await Task.Delay(300, cancel).ConfigureAwait(false);
        }
    }

}
