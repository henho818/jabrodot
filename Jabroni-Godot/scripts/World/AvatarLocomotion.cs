using System;
using Godot;

namespace Jabroni.World;

/// <summary>
/// Avatar-specific locomotion: MoveTo can also specify a Node3D to face once arrived (e.g.
/// an NPC being approached), firing ArrivedAtNpc once when that happens so other systems
/// (ClickToMove) can hand off to the NPC's own AI.
/// </summary>
public partial class AvatarLocomotion : NavMeshLocomotion
{
    public event Action<Node3D> ArrivedAtNpc;

    private Node3D _approachTarget;
    private bool _arrivalNotified;

    public void MoveTo(Vector3 destination, Node3D faceOnArrival)
    {
        _approachTarget = faceOnArrival;
        _arrivalNotified = false;
        MoveTo(destination);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (_approachTarget == null || !HasArrived)
        {
            return;
        }

        FaceWorldPosition(_approachTarget.GlobalPosition);

        if (!_arrivalNotified)
        {
            _arrivalNotified = true;
            ArrivedAtNpc?.Invoke(_approachTarget);
        }
    }
}
