using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class ReleaseUpdate
{
    private const string Releases = "https://github.com/bfgd2jg/lol-international-chinese-voice-tool/releases/";
    private const string SetupName = "LOL-Chinese-Voice-Tool-Setup.exe";
    private const int MaxInstallerSize = 25 * 1024 * 1024;

    static ReleaseUpdate()
    {
        // The standalone .NET Framework build otherwise inherits legacy TLS defaults.
        AppContext.SetSwitch("Switch.System.Net.DontEnableSchUseStrongCrypto", false);
        AppContext.SetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", false);
    }

    public Version Version { get; private set; }
    public string InstallerPath { get; private set; }
    private Manifest DownloadedManifest { get; set; }

    public sealed class Manifest
    {
        public string Version { get; set; }
        public string Sha256 { get; set; }
        public int Size { get; set; }
    }

    internal static Version GetNewVersion(Manifest manifest, Version current)
    {
        Version version;
        if (manifest == null || !Regex.IsMatch(manifest.Version ?? "", @"\A\d+\.\d+\.\d+\z") ||
            !Version.TryParse(manifest.Version + ".0", out version) ||
            !Regex.IsMatch(manifest.Sha256 ?? "", @"\A[0-9a-fA-F]{64}\z") ||
            manifest.Size <= 0 || manifest.Size > MaxInstallerSize)
            throw new InvalidDataException("Invalid update manifest.");
        return version > current ? version : null;
    }

    public static async Task<ReleaseUpdate> DownloadLatest(Version current, string appDirectory, CancellationToken cancel)
    {
        string updateDirectory = Path.Combine(appDirectory, "updates");
        string installer = Path.Combine(updateDirectory, SetupName);
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel))
        using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
        {
            limit.CancelAfter(TimeSpan.FromSeconds(45));
            CancellationToken token = limit.Token;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LoL-Chinese-Voice-Tool");
            byte[] json = await ReadAsset(client, Releases + "latest/download/update.json", 4096, token).ConfigureAwait(false);
            var manifest = new JavaScriptSerializer().Deserialize<Manifest>(System.Text.Encoding.UTF8.GetString(json));
            Version version = GetNewVersion(manifest, current);
            if (version == null)
            {
                try { File.Delete(installer); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return null;
            }

            byte[] data = await ReadAsset(client, Releases + "download/v" + manifest.Version + "/" + SetupName,
                manifest.Size, token).ConfigureAwait(false);
            using (var stream = new MemoryStream(data, false)) VerifyInstaller(stream, manifest);
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(updateDirectory);
            string pending = installer + ".tmp";
            try
            {
                File.WriteAllBytes(pending, data);
                if (File.Exists(installer)) File.Replace(pending, installer, null);
                else File.Move(pending, installer);
            }
            finally { File.Delete(pending); }
            return new ReleaseUpdate { Version = version, InstallerPath = installer, DownloadedManifest = manifest };
        }
    }

    private static async Task<byte[]> ReadAsset(HttpClient client, string address, int limit, CancellationToken cancel)
    {
        var uri = new Uri(address);
        for (int redirect = 0; redirect < 6; redirect++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                (uri.Host != "github.com" && uri.Host != "release-assets.githubusercontent.com" &&
                 uri.Host != "objects.githubusercontent.com"))
                throw new InvalidDataException("Unexpected update host.");

            using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
            {
                int status = (int)response.StatusCode;
                if (status >= 300 && status < 400 && response.Headers.Location != null)
                {
                    uri = new Uri(uri, response.Headers.Location);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update is too large.");
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new MemoryStream())
                using (cancel.Register(() => input.Dispose()))
                {
                    var buffer = new byte[16384];
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) != 0)
                    {
                        if (output.Length + count > limit) throw new InvalidDataException("Update is too large.");
                        output.Write(buffer, 0, count);
                    }
                    return output.ToArray();
                }
            }
        }
        throw new InvalidDataException("Too many update redirects.");
    }

    public void Install(string language)
    {
        var info = new ProcessStartInfo(InstallerPath,
            "/SILENT /SP- /NORESTART /UPDATE=1 /LANG=" + (language == "zh-CN" ? "chinesesimplified" : "english")) {
            UseShellExecute = true
        };
        using (var stream = File.Open(InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            VerifyInstaller(stream, DownloadedManifest);
            using (Process process = Process.Start(info)) { }
        }
    }

    internal static void VerifyInstaller(Stream stream, Manifest manifest)
    {
        using (var sha256 = SHA256.Create())
        {
            if (stream.Length != manifest.Size) throw new InvalidDataException("Installer size mismatch.");
            string digest = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "");
            if (!String.Equals(digest, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installer integrity check failed.");
        }
    }
}
