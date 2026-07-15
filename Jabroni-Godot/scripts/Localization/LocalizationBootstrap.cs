using Godot;
using Jabroni.Data;

namespace Jabroni.Localization;

/// <summary>
/// Builds Godot Translation resources from data/Localization.tsv and registers them with
/// TranslationServer, so the rest of the game can use Godot's built-in Tr()/auto-translate
/// instead of a bespoke localization service -- locale switching and live UI re-translation
/// come for free from the engine this way.
/// </summary>
public static class LocalizationBootstrap
{
    private const string DataFilePath = "res://data/Localization.tsv";
    private const string DefaultLocale = "en";
    private static readonly string[] Locales = { "en", "zh", "ja", "es" };

    public static void Load()
    {
        var rows = TsvTable.Load(DataFilePath);
        if (rows.Count == 0)
        {
            return;
        }

        foreach (string locale in Locales)
        {
            var translation = new Translation { Locale = locale };
            foreach (var pair in rows)
            {
                string message = pair.Value.GetString(locale);
                if (!string.IsNullOrEmpty(message))
                {
                    translation.AddMessage(pair.Key, message);
                }
            }

            TranslationServer.AddTranslation(translation);
        }

        TranslationServer.SetLocale(DefaultLocale);
    }
}
