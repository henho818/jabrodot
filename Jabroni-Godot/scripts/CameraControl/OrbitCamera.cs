using Godot;
using Jabroni.Core;

namespace Jabroni.CameraControl;

/// <summary>
/// Scroll-wheel zoom + right-drag orbit rig. Distance is clamped and the pitch angle
/// interpolates between MinPitchDegrees (zoomed out) and MaxPitchDegrees (zoomed in),
/// reproducing the feel of the source project's CameraZoomController. Tuning values are
/// placeholders scaled to this scene's greybox village and will likely need revisiting
/// once real art/scale is in place.
/// </summary>
public partial class OrbitCamera : Node3D
{
    [Export] public float MinDistance { get; set; } = 20f;
    [Export] public float MaxDistance { get; set; } = 60f;
    [Export] public float MinPitchDegrees { get; set; } = 30f;
    [Export] public float MaxPitchDegrees { get; set; } = 45f;
    [Export] public float ZoomStep { get; set; } = 4f;
    [Export] public float OrbitSensitivity { get; set; } = 0.3f;

    private Node3D _pitchPivot;
    private Camera3D _camera;
    private float _distance;

    public override void _Ready()
    {
        _pitchPivot = GetNode<Node3D>("PitchPivot");
        _camera = _pitchPivot.GetNode<Camera3D>("Camera3D");
        _distance = (MinDistance + MaxDistance) * 0.5f;
        ApplyDistance();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.IsActionPressed(InputActions.CameraOrbit))
        {
            RotateY(Mathf.DegToRad(-motion.Relative.X * OrbitSensitivity));
        }
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed(InputActions.CameraZoomIn))
        {
            _distance -= ZoomStep;
        }

        if (Input.IsActionJustPressed(InputActions.CameraZoomOut))
        {
            _distance += ZoomStep;
        }

        _distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);
        ApplyDistance();
    }

    private void ApplyDistance()
    {
        float t = Mathf.InverseLerp(MinDistance, MaxDistance, _distance);
        float pitch = Mathf.Lerp(MaxPitchDegrees, MinPitchDegrees, t);
        _pitchPivot.RotationDegrees = new Vector3(-pitch, 0f, 0f);
        _camera.Position = new Vector3(0f, 0f, _distance);
    }
}
