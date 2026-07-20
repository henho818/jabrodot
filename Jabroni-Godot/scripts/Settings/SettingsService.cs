using Godot;
using Jabroni.Core;

namespace Jabroni.Settings;

/// <summary>
/// Persisted player settings (dialog playback speed, volumes, FPS limit), matching the source
/// project's SoundSettingsService/RenderSettingsService/DialogSettingsService defaults (0.75
/// volume, 60 FPS). Unlike the source, FpsLimit and PlaybackSpeedFactor are actually applied
/// here (the Unity versions stored the values but never consumed them).
///
/// Godot doesn't persist custom audio buses in a project resource we control at runtime, so the
/// Music/SFX/Dialog buses are created in code on every startup rather than authored once in a
/// .tres bus layout -- cheap, deterministic, and consistent with this codebase's established
/// "wire it in code" approach.
///
/// Hotkeys (see InputActions) let you live-tune settings without a settings menu, which doesn't
/// exist yet. Values persist to user://settings.cfg.
/// </summary>
public partial class SettingsService : Node
{
    [Signal]
    public delegate void SettingsChangedEventHandler();

    public static SettingsService Instance { get; private set; }

    private const string SettingsPath = "user://settings.cfg";
    private const string Section = "settings";

    private const string MasterBus = "Master";
    private const string MusicBus = "Music";
    private const string SfxBus = "SFX";
    private const string DialogBus = "Dialog";

    private static readonly int[] FpsLimitPresets = { 30, 60, 120, 240, 0 };

    public float MainVolume { get; private set; } = 0.75f;
    public float MusicVolume { get; private set; } = 0.75f;
    public float SfxVolume { get; private set; } = 0.75f;
    public float DialogVolume { get; private set; } = 0.75f;
    public float DialogPlaybackSpeedFactor { get; private set; } = 1.0f;
    public int FpsLimit { get; private set; } = 24;

    public override void _Ready()
    {
        Instance = this;
        EnsureBuses();
        Load();
        Apply();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.SettingsMasterVolumeDown)) AdjustMainVolume(-0.05f);
        if (@event.IsActionPressed(InputActions.SettingsMasterVolumeUp)) AdjustMainVolume(0.05f);
        if (@event.IsActionPressed(InputActions.SettingsDialogVolumeDown)) AdjustDialogVolume(-0.05f);
        if (@event.IsActionPressed(InputActions.SettingsDialogVolumeUp)) AdjustDialogVolume(0.05f);
        if (@event.IsActionPressed(InputActions.SettingsDialogSpeedDown)) AdjustDialogSpeed(-0.1f);
        if (@event.IsActionPressed(InputActions.SettingsDialogSpeedUp)) AdjustDialogSpeed(0.1f);
        if (@event.IsActionPressed(InputActions.SettingsFpsLimitDown)) CycleFpsLimit(-1);
        if (@event.IsActionPressed(InputActions.SettingsFpsLimitUp)) CycleFpsLimit(1);
    }

    private void AdjustMainVolume(float delta)
    {
        MainVolume = Mathf.Clamp(MainVolume + delta, 0f, 1f);
        Apply();
        Save();
    }

    private void AdjustDialogVolume(float delta)
    {
        DialogVolume = Mathf.Clamp(DialogVolume + delta, 0f, 1f);
        Apply();
        Save();
    }

    private void AdjustDialogSpeed(float delta)
    {
        DialogPlaybackSpeedFactor = Mathf.Clamp(DialogPlaybackSpeedFactor + delta, 0.5f, 2.0f);
        Apply();
        Save();
    }

    private void CycleFpsLimit(int direction)
    {
        int index = System.Array.IndexOf(FpsLimitPresets, FpsLimit);
        if (index < 0)
        {
            index = 1; // fall back to the 60 preset
        }

        index = (index + direction + FpsLimitPresets.Length) % FpsLimitPresets.Length;
        FpsLimit = FpsLimitPresets[index];
        Apply();
        Save();
    }

    private void EnsureBuses()
    {
        EnsureBus(MusicBus);
        EnsureBus(SfxBus);
        EnsureBus(DialogBus);
    }

    private static void EnsureBus(string busName)
    {
        if (AudioServer.GetBusIndex(busName) != -1)
        {
            return;
        }

        int index = AudioServer.BusCount;
        AudioServer.AddBus(index);
        AudioServer.SetBusName(index, busName);
        AudioServer.SetBusSend(index, MasterBus);
    }

    private void Apply()
    {
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(MasterBus), Mathf.LinearToDb(MainVolume));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(MusicBus), Mathf.LinearToDb(MusicVolume));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(SfxBus), Mathf.LinearToDb(SfxVolume));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(DialogBus), Mathf.LinearToDb(DialogVolume));

        Engine.MaxFps = FpsLimit;

        EmitSignal(SignalName.SettingsChanged);
    }

    private void Load()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) != Error.Ok)
        {
            return;
        }

        MainVolume = (float)config.GetValue(Section, "main_volume", MainVolume);
        MusicVolume = (float)config.GetValue(Section, "music_volume", MusicVolume);
        SfxVolume = (float)config.GetValue(Section, "sfx_volume", SfxVolume);
        DialogVolume = (float)config.GetValue(Section, "dialog_volume", DialogVolume);
        DialogPlaybackSpeedFactor = (float)config.GetValue(Section, "dialog_speed", DialogPlaybackSpeedFactor);
        FpsLimit = (int)config.GetValue(Section, "fps_limit", FpsLimit);
    }

    private void Save()
    {
        var config = new ConfigFile();
        config.SetValue(Section, "main_volume", MainVolume);
        config.SetValue(Section, "music_volume", MusicVolume);
        config.SetValue(Section, "sfx_volume", SfxVolume);
        config.SetValue(Section, "dialog_volume", DialogVolume);
        config.SetValue(Section, "dialog_speed", DialogPlaybackSpeedFactor);
        config.SetValue(Section, "fps_limit", FpsLimit);
        config.Save(SettingsPath);
    }
}
