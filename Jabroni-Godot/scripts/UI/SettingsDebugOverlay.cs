using Godot;
using Jabroni.Data;
using Jabroni.Settings;

namespace Jabroni.UI;

/// <summary>
/// Small always-on corner readout of the live-tunable settings and their hotkeys, standing in
/// for a real settings menu (none exists yet). Refreshes whenever SettingsService changes.
/// <para>
/// Language gets a dropdown rather than a hotkey pair like the others: cycling through six
/// locales two keys at a time is a poor way to reach one, and unlike a volume you can't tell
/// where you are in the list without seeing it.
/// </para>
/// </summary>
public partial class SettingsDebugOverlay : Control
{
    private Label _label;
    private OptionButton _language;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
        _language = GetNode<OptionButton>("Language");

        // Item ids are indices into DialogSchema.Locales, so the selection maps back to a locale
        // without parsing the display name -- which is localised and would be a poor key.
        for (int i = 0; i < DialogSchema.Locales.Length; i++)
        {
            string locale = DialogSchema.Locales[i];
            _language.AddItem($"{TranslationServer.GetLocaleName(locale)} ({locale})", i);
        }

        _language.ItemSelected += OnLanguageSelected;

        var settings = GetNode<SettingsService>("/root/SettingsService");
        settings.SettingsChanged += Refresh;
        Refresh();
    }

    private void OnLanguageSelected(long index)
    {
        GetNode<SettingsService>("/root/SettingsService")
            .SetLocale(DialogSchema.Locales[(int)_language.GetItemId((int)index)]);
    }

    private void Refresh()
    {
        var settings = GetNode<SettingsService>("/root/SettingsService");
        string fps = settings.FpsLimit == 0 ? "Unlimited" : settings.FpsLimit.ToString();

        _label.Text =
            $"[1/2] Master Volume: {settings.MainVolume:P0}\n" +
            $"[3/4] Dialog Volume: {settings.DialogVolume:P0}\n" +
            $"[5/6] Dialog Speed: {settings.DialogPlaybackSpeedFactor:F1}x\n" +
            $"[7/8] FPS Limit: {fps}";

        // Assigning Selected doesn't re-emit ItemSelected, so this can't loop back into
        // SetLocale; it just keeps the dropdown honest if the locale is changed from elsewhere.
        int current = System.Array.IndexOf(DialogSchema.Locales, settings.Locale);
        if (current >= 0)
        {
            _language.Selected = current;
        }
    }
}
