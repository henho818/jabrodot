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
    [Export] public float ZoomDuration { get; set; } = 0.25f;
    [Export] public float OrbitSensitivity { get; set; } = 0.3f;
    [Export] public float FollowSmoothing { get; set; } = 6f;

    /// <summary>Node the rig smoothly tracks each frame (set from code -- see ClickToMove --
    /// since exported NodePath overrides don't apply on this Godot build).</summary>
    public Node3D FollowTarget { get; set; }

    private Node3D _pitchPivot;
    private Camera3D _camera;

    // _targetDistance is the clamped result of the latest scroll input; _displayedDistance is
    // what's actually rendered, eased toward the target over ZoomDuration each time it changes
    // (see AnimateZoomTo) instead of snapping per scroll tick.
    private float _targetDistance;
    private float _displayedDistance;
    private Tween _zoomTween;

    public override void _Ready()
    {
        _pitchPivot = GetNode<Node3D>("PitchPivot");
        _camera = _pitchPivot.GetNode<Camera3D>("Camera3D");
        _targetDistance = _displayedDistance = (MinDistance + MaxDistance) * 0.5f;
        ApplyDistance();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.IsActionPressed(InputActions.CameraOrbit))
        {
            RotateY(Mathf.DegToRad(-motion.Relative.X * OrbitSensitivity));
        }
    }

    /// <summary>
    /// Zoom runs off unhandled input rather than polling Input.IsActionJustPressed each frame, so
    /// that the UI can outrank it -- a notch the dialog box has already scrolled with is marked
    /// handled and never arrives here.
    /// <para>
    /// Polling could not be made to respect that: the Input singleton's action state is set from
    /// the raw event regardless of who consumed it, so a scroll over the dialog would move the
    /// lines and zoom the world on the same notch no matter what the UI did with the event.
    /// </para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        int steps = 0;

        if (@event.IsActionPressed(InputActions.CameraZoomIn))
        {
            steps--;
        }

        if (@event.IsActionPressed(InputActions.CameraZoomOut))
        {
            steps++;
        }

        if (steps == 0)
        {
            return;
        }

        _targetDistance = Mathf.Clamp(_targetDistance + (steps * ZoomStep), MinDistance, MaxDistance);
        AnimateZoomTo(_targetDistance);
    }

    public override void _Process(double delta)
    {
        ApplyDistance();

        if (FollowTarget != null)
        {
            float weight = 1f - Mathf.Exp(-FollowSmoothing * (float)delta);
            Position = Position.Lerp(FollowTarget.GlobalPosition, weight);
        }
    }

    private void AnimateZoomTo(float target)
    {
        _zoomTween?.Kill();
        _zoomTween = CreateTween();
        _zoomTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _zoomTween.TweenMethod(Callable.From<float>(d => _displayedDistance = d), _displayedDistance, target, ZoomDuration);
    }

    private void ApplyDistance()
    {
        float t = Mathf.InverseLerp(MinDistance, MaxDistance, _displayedDistance);
        float pitch = Mathf.Lerp(MinPitchDegrees, MaxPitchDegrees, t);
        _pitchPivot.RotationDegrees = new Vector3(-pitch, 0f, 0f);
        _camera.Position = new Vector3(0f, 0f, _displayedDistance);
    }
}
