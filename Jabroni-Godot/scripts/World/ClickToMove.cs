using Godot;
using Jabroni.AI;
using Jabroni.CameraControl;
using Jabroni.Core;
using Jabroni.UI.Dialog;

namespace Jabroni.World;

/// <summary>
/// Raycasts from the mouse against the Pathable and Agent physics layers on click and
/// drives the avatar's own AgentAI: ground clicks move it directly (canceling any chat
/// approach in progress), NPC clicks set a chat target so the avatar's FSM (Idle ->
/// NavToAgent -> Chatting) takes over the approach-and-face sequence itself.
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

    private AvatarAgentAI _avatarAi;
    private Camera3D _camera;
    private PackedScene _destinationCursorScene;

    public override void _Ready()
    {
        _avatarAi = GetNode<AvatarAgentAI>("Avatar/AgentAI");
        _camera = GetNode<Camera3D>("CameraRig/PitchPivot/Camera3D");
        _destinationCursorScene = GD.Load<PackedScene>(DestinationCursorScenePath);

        GetNode<OrbitCamera>("CameraRig").FollowTarget = _avatarAi.Body;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed(InputActions.ClickMove))
        {
            return;
        }

        // The point comes off the event rather than from Viewport.GetMousePosition(), which on the
        // root viewport reports the OS cursor -- and a finger never moves that. A tap arrives here
        // as an emulated mouse button carrying the position actually touched, while the cursor is
        // still wherever a real mouse last left it, so polling it raycast from somewhere else
        // entirely: usually into nothing, and the tap did nothing at all. A real click worked only
        // because the cursor happened to already be under it.
        //
        // Kept as a fallback for a ClickMove rebound to something that carries no position (a key,
        // a pad button), where the cursor is the only point on offer.
        Vector2 point = @event is InputEventMouse mouse
            ? mouse.Position
            : GetViewport().GetMousePosition();

        TryClick(point);
    }

    private void TryClick(Vector2 screenPoint)
    {
        // The dialog's backdrop is a MOUSE_FILTER_STOP Control, so a press over it is consumed by
        // GUI dispatch and normally never reaches _UnhandledInput at all. This is the second lock
        // on the same door: it holds for the points that don't arrive by that route -- the cursor
        // fallback above, for a ClickMove rebound to something carrying no position -- and it means
        // the rule "the darkened region is not the world" is stated where the world click is made,
        // rather than resting entirely on hit-testing a rect that moves every frame.
        if (DialogBox.Instance?.Host?.BlocksPointer(screenPoint) == true)
        {
            return;
        }

        Vector3 origin = _camera.ProjectRayOrigin(screenPoint);
        Vector3 direction = _camera.ProjectRayNormal(screenPoint);

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
            _avatarAi.ChatTarget = collider;
            SpawnCursor(collider.GlobalPosition);
        }
        else
        {
            _avatarAi.MoveToGround(hitPosition);
            SpawnCursor(hitPosition);
        }
    }

    private void SpawnCursor(Vector3 position)
    {
        var cursor = _destinationCursorScene.Instantiate<Node3D>();
        GetTree().CurrentScene.AddChild(cursor);
        cursor.GlobalPosition = position + new Vector3(0f, 0.03f, 0f);
    }
}
