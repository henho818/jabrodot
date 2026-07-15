using Godot;
using Jabroni.Core;

namespace Jabroni.CameraControl;

/// <summary>
/// Scroll-wheel zoom + right-drag orbit rig. Distance is clamped and the pitch angle
/// interpolates between MinPitchDegrees (zoomed in, shallow/eye-level) and
/// MaxPitchDegrees (zoomed out, steeper/more top-down) -- matching the source project's
/// CameraZoomController exactly, including the performance intent: a steeper downward
/// angle when zoomed out means less sky/horizon is in view, so there's less far scenery
/// to render right when the camera would otherwise be seeing the most of it.
/// </summary>
public partial class OrbitCamera : Node3D
{
    [Export] public float MinDistance { get; set; } = 20f;
    [Export] public float MaxDistance { get; set; } = 60f;
    [Export] public float MinPitchDegrees { get; set; } = 20f;
    [Export] public float MaxPitchDegrees { get; set; } = 45f;
    [Export] public float ZoomStep { get; set; } = 4f;
    [Export] public float OrbitSensitivity { get; set; } = 0.3f;
    [Export] public float FollowSmoothing { get; set; } = 6f;

    /// <summary>Node the rig smoothly tracks each frame (set from code -- see ClickToMove --
    /// since exported NodePath overrides don't apply on this Godot build).</summary>
    public Node3D FollowTarget { get; set; }

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

        if (FollowTarget != null)
        {
            float weight = 1f - Mathf.Exp(-FollowSmoothing * (float)delta);
            Position = Position.Lerp(FollowTarget.GlobalPosition, weight);
        }
    }

    private void ApplyDistance()
    {
        float t = Mathf.InverseLerp(MinDistance, MaxDistance, _distance);
        float pitch = Mathf.Lerp(MinPitchDegrees, MaxPitchDegrees, t);
        _pitchPivot.RotationDegrees = new Vector3(-pitch, 0f, 0f);
        _camera.Position = new Vector3(0f, 0f, _distance);
    }
}
