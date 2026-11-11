using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class LocaleFile
{
    private static readonly Encoding Bytes = Encoding.GetEncoding(28591);
    private static readonly Regex Settings = new Regex(@"(?m)^settings:[ \t]*(?:#[^\r\n]*)?\r?$");
    private static readonly Regex DirectKey = new Regex(@"(?m)^( +)[A-Za-z_][A-Za-z_0-9]*:");
    private static readonly Regex Locale = new Regex(
        @"(?m)^( +)locale:[ \t]*([""']?)(?<code>[a-z]{2}_[A-Z]{2})\2(?=[ \t]*(?:#[^\r\n]*)?\r?$)");
    private static readonly Regex Code = new Regex(@"\A[a-z]{2}_[A-Z]{2}\z");
    private readonly string file;

    public LocaleFile(string filePath) { file = filePath; }

    internal static Match FindLocale(string content)
    {
        // Latin-1 preserves all bytes. Skip a possible UTF-8 BOM when matching the first key.
        int bom = content.StartsWith("\u00ef\u00bb\u00bf", StringComparison.Ordinal) ? 3 : 0;
        MatchCollection headers = Settings.Matches(content.Substring(bom));
        if (headers.Count != 1) throw new InvalidDataException("ErrorSettingsSection");
        int start = headers[0].Index + headers[0].Length + bom;
        Match next = Regex.Match(content.Substring(start), @"(?m)^[A-Za-z_][A-Za-z_0-9]*:");
        int end = next.Success ? start + next.Index : content.Length;
        string segment = content.Substring(start, end - start);
        var keys = DirectKey.Matches(segment).Cast<Match>().ToArray();
        if (keys.Length == 0) throw new InvalidDataException("ErrorSettingsEmpty");
        int indent = keys.Min(m => m.Groups[1].Length);
        var matches = Locale.Matches(content).Cast<Match>()
            .Where(m => m.Index >= start && m.Index < end && m.Groups[1].Length == indent).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("ErrorLocaleFormat");
        return matches[0];
    }

    public string Read()
    {
        string text;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Bytes, false))
            text = reader.ReadToEnd();
        return FindLocale(text).Groups["code"].Value;
    }

    public bool TryWrite(string value, string onlyIf = null)
    {
        if (!Code.IsMatch(value)) throw new ArgumentException("ErrorLocaleCode");
        FileStream stream;
        try { stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return false; } // Riot may be replacing the file.
        using (stream)
        {
            var original = new byte[checked((int)stream.Length)];
            for (int offset = 0; offset < original.Length;)
            {
                int count = stream.Read(original, offset, original.Length - offset);
                if (count == 0) throw new EndOfStreamException("ErrorConfigRead");
                offset += count;
            }
            Group code = FindLocale(Bytes.GetString(original)).Groups["code"];
            if (onlyIf != null && code.Value != onlyIf) return true;
            if (code.Value == value) return true;
            // Locale codes have the same byte length. No other YAML bytes are rewritten.
            byte[] changed = Bytes.GetBytes(value);
            stream.Position = code.Index;
            stream.Write(changed, 0, changed.Length);
            stream.Flush();
            return true;
        }
    }
}
