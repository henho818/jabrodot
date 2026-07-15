using Godot;
using Jabroni.Settings;

namespace Jabroni.UI;

/// <summary>
/// Small always-on corner readout of the live-tunable settings and their hotkeys, standing in
/// for a real settings menu (none exists yet). Refreshes whenever SettingsService changes.
/// </summary>
public partial class SettingsDebugOverlay : Control
{
    private Label _label;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");

        var settings = GetNode<SettingsService>("/root/SettingsService");
        settings.SettingsChanged += Refresh;
        Refresh();
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
    }
}
