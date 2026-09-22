using Godot;

namespace Jabroni.Core;

/// <summary>
/// Registers project input actions in code instead of hand-authored project.godot
/// InputMap resource syntax, so bindings stay simple to read and safe to change.
/// </summary>
public static class InputActions
{
    /// <summary>
    /// Device id meaning "any device". Every registered event needs it explicitly. An InputEvent
    /// built in code defaults to a device id that only matches real hardware -- the editor's Input
    /// Map fills this in for you, and building the events here skips that. Left at the default, an
    /// action ignores anything Godot tags InputEvent.DEVICE_ID_EMULATION (-1), which is exactly
    /// what a finger becomes: emulate_mouse_from_touch turns a touch into a left-click carrying
    /// that id, so taps matched no action at all while the mouse worked fine.
    /// </summary>
    private const int AllDevices = -1;

    public const string CameraZoomIn = "camera_zoom_in";
    public const string CameraZoomOut = "camera_zoom_out";
    public const string CameraOrbit = "camera_orbit";
    public const string ClickMove = "click_move";

    // Debug hotkeys for live-tuning SettingsService values -- no settings menu exists yet.
    public const string SettingsMasterVolumeDown = "settings_master_volume_down";
    public const string SettingsMasterVolumeUp = "settings_master_volume_up";
    public const string SettingsDialogVolumeDown = "settings_dialog_volume_down";
    public const string SettingsDialogVolumeUp = "settings_dialog_volume_up";
    public const string SettingsDialogSpeedDown = "settings_dialog_speed_down";
    public const string SettingsDialogSpeedUp = "settings_dialog_speed_up";
    public const string SettingsFpsLimitDown = "settings_fps_limit_down";
    public const string SettingsFpsLimitUp = "settings_fps_limit_up";

    public static void Register()
    {
        AddMouseButtonAction(CameraZoomIn, MouseButton.WheelUp);
        AddMouseButtonAction(CameraZoomOut, MouseButton.WheelDown);
        AddMouseButtonAction(CameraOrbit, MouseButton.Right);
        AddMouseButtonAction(ClickMove, MouseButton.Left);

        AddKeyAction(SettingsMasterVolumeDown, Key.Key1);
        AddKeyAction(SettingsMasterVolumeUp, Key.Key2);
        AddKeyAction(SettingsDialogVolumeDown, Key.Key3);
        AddKeyAction(SettingsDialogVolumeUp, Key.Key4);
        AddKeyAction(SettingsDialogSpeedDown, Key.Key5);
        AddKeyAction(SettingsDialogSpeedUp, Key.Key6);
        AddKeyAction(SettingsFpsLimitDown, Key.Key7);
        AddKeyAction(SettingsFpsLimitUp, Key.Key8);
    }

    private static void AddMouseButtonAction(string action, MouseButton button)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button, Device = AllDevices });
    }

    private static void AddKeyAction(string action, Key key)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { Keycode = key, Device = AllDevices });
    }
}
