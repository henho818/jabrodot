using Godot;
using Jabroni.Core;

namespace Jabroni.World;

/// <summary>
/// Raycasts from the mouse against the Pathable and Agent physics layers on click.
/// Clicking the ground walks the avatar there; clicking an agent (e.g. an NPC) walks
/// the avatar to social distance and faces it, in place of a proper dialog trigger
/// (that arrives in a later milestone).
///
/// Node wiring is hardcoded here (relative paths + GD.Load) rather than exposed via
/// [Export], because this Godot build (4.7 stable, Windows ARM64, Mono) does not apply
/// C# exported-property overrides from .tscn node blocks (verified: the identical
/// override works with GDScript's @export, so it's specific to the C# binding layer on
/// this platform). Revisit if/when that's fixed upstream.
/// </summary>
public partial class ClickToMove : Node3D
{
    private const string DestinationCursorScenePath = "res://scenes/DestinationCursor.tscn";
    private const uint ClickableMask = PhysicsLayers.Pathable | PhysicsLayers.Agent;
    private const float SocialDistance = 2.5f;

    private AvatarLocomotion _avatar;
    private Camera3D _camera;
    private PackedScene _destinationCursorScene;

    public override void _Ready()
    {
        _avatar = GetNode<AvatarLocomotion>("Avatar");
        _camera = GetNode<Camera3D>("CameraRig/PitchPivot/Camera3D");
        _destinationCursorScene = GD.Load<PackedScene>(DestinationCursorScenePath);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.ClickMove))
        {
            TryClick();
        }
    }

    private void TryClick()
    {
        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector3 origin = _camera.ProjectRayOrigin(mousePos);
        Vector3 direction = _camera.ProjectRayNormal(mousePos);

        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * 1000f, ClickableMask);
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            return;
        }

        var hitPosition = (Vector3)result["position"];
        var collider = (Node3D)result["collider"];

        if (collider is CollisionObject3D co && (co.CollisionLayer & PhysicsLayers.Agent) != 0)
        {
            ApproachAgent(co);
        }
        else
        {
            _avatar.MoveTo(hitPosition);
            SpawnCursor(hitPosition);
        }
    }

    private void ApproachAgent(CollisionObject3D agent)
    {
        Vector3 agentPos = agent.GlobalPosition;
        Vector3 away = _avatar.GlobalPosition - agentPos;
        away.Y = 0f;
        if (away.LengthSquared() < 0.0001f)
        {
            away = Vector3.Forward;
        }

        Vector3 stopPoint = agentPos + away.Normalized() * SocialDistance;

        _avatar.MoveTo(stopPoint, agent);
        SpawnCursor(stopPoint);
    }

    private void SpawnCursor(Vector3 position)
    {
        var cursor = _destinationCursorScene.Instantiate<Node3D>();
        GetTree().CurrentScene.AddChild(cursor);
        cursor.GlobalPosition = position + new Vector3(0f, 0.03f, 0f);
    }
}
