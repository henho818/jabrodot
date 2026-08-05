using Godot;
using Jabroni.Data;

namespace Jabroni.Localization;

/// <summary>
/// Builds Godot Translation resources from data/Localization.tsv and registers them with
/// TranslationServer, so the rest of the game can use Godot's built-in Tr()/auto-translate
/// instead of a bespoke localization service -- locale switching and live UI re-translation
/// come for free from the engine this way.
/// <para>
/// Registering is all this does. Which locale is *active* is SettingsService's, so that the
/// player's saved language isn't overwritten by a default every time the game starts.
/// </para>
/// </summary>
public static class LocalizationBootstrap
{
	private static readonly string[] Locales = DialogSchema.Locales;

	public static void Load()
	{
		var rows = TsvTable.Load(DataPaths.Localization);
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
	}
}
