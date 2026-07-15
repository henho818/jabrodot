using Godot;

namespace Jabroni.Core;

/// <summary>
/// Registers project input actions in code instead of hand-authored project.godot
/// InputMap resource syntax, so bindings stay simple to read and safe to change.
/// </summary>
public static class InputActions
{
    public const string CameraZoomIn = "camera_zoom_in";
    public const string CameraZoomOut = "camera_zoom_out";
    public const string CameraOrbit = "camera_orbit";
    public const string ClickMove = "click_move";

    public static void Register()
    {
        AddMouseButtonAction(CameraZoomIn, MouseButton.WheelUp);
        AddMouseButtonAction(CameraZoomOut, MouseButton.WheelDown);
        AddMouseButtonAction(CameraOrbit, MouseButton.Right);
        AddMouseButtonAction(ClickMove, MouseButton.Left);
    }

    private static void AddMouseButtonAction(string action, MouseButton button)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }
}
