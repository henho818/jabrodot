using Godot;
using Jabroni.AI;
using Jabroni.CameraControl;
using Jabroni.Core;

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
    private const float RayLength = 1000f;

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

        Vector3 rayEnd = origin + direction * RayLength;

        // Agents are still picked with a physics ray -- they're bodies, not navmesh. Pathable
        // stays in the mask so terrain occludes an NPC standing behind a hill.
        var query = PhysicsRayQueryParameters3D.Create(origin, rayEnd, ClickableMask);
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);

        if (result.Count > 0
            && (Node3D)result["collider"] is CollisionObject3D co
            && (co.CollisionLayer & PhysicsLayers.Agent) != 0)
        {
            _avatarAi.ChatTarget = co;
            SpawnCursor(co.GlobalPosition);
            return;
        }

        // Ground clicks resolve against the navmesh itself rather than against collision
        // geometry, so a destination is walkable by construction instead of being a physics
        // hit we try to repair afterwards. Clicking a rooftop, a slope too steep to have been
        // baked, or straight through a building now simply doesn't register -- which is also
        // what a player expects from clicking somewhere their character can't stand.
        if (!NavMeshSnap.TryRaycast(GetWorld3D().NavigationMap, origin, rayEnd, out Vector3 destination))
        {
            return;
        }

        _avatarAi.MoveToGround(destination);
        SpawnCursor(destination);
    }

    private void SpawnCursor(Vector3 position)
    {
        var cursor = _destinationCursorScene.Instantiate<Node3D>();
        GetTree().CurrentScene.AddChild(cursor);
        cursor.GlobalPosition = position + new Vector3(0f, 0.03f, 0f);
    }
}
