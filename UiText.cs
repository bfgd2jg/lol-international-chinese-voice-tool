using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;

internal sealed class UiText
{
    private static readonly Dictionary<string, string[]> Translations = ReadTranslations();
    private readonly int column;
    private readonly CultureInfo culture;

    public UiText(string language)
    {
        column = language == "en-US" ? 1 : 0;
        culture = CultureInfo.GetCultureInfo(column == 1 ? "en-US" : "zh-CN");
    }

    private static Dictionary<string, string[]> ReadTranslations()
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Launcher.UiText.tsv")))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                string[] fields = line.Split('\t');
                if (fields.Length != 3) throw new InvalidDataException("Invalid translation row: " + fields[0]);
                result.Add(fields[0], new[] { fields[1], fields[2] });
            }
        }
        return result;
    }

    public static bool HasKey(string key) { return Translations.ContainsKey(key); }

    public string Get(string key, params object[] arguments)
    {
        string[] translations;
        string format = Translations.TryGetValue(key, out translations) ? translations[column] : key;
        if (arguments.Length == 0) return format;
        var values = new object[arguments.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var message = arguments[i] as UiMessage;
            values[i] = message == null ? arguments[i] : message.Render(this);
        }
        return String.Format(culture, format, values);
    }

    public void Apply(Window window)
    {
        window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(culture.Name);
        foreach (var translation in Translations)
            window.Resources[translation.Key] = translation.Value[column];
    }
}

internal sealed class UiMessage
{
    private readonly string key;
    private readonly object[] arguments;

    public UiMessage(string key, params object[] arguments)
    {
        this.key = key;
        this.arguments = arguments;
    }

    public static implicit operator UiMessage(string key) { return new UiMessage(key); }
    public string Render(UiText text) { return text.Get(key, arguments); }

    public static UiMessage Locale(string code)
    {
        string localeKey = "Locale." + code;
        return new UiMessage(UiText.HasKey(localeKey) ? localeKey : code);
    }
}
