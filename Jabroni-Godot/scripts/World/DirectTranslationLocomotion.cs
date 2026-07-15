using Godot;

namespace Jabroni.World;

/// <summary>
/// Straight-line movement ignoring the navmesh entirely -- used by agents that move
/// through open space rather than on a baked walkable surface (the shark, swimming
/// underwater, mirroring the source project's off-mesh shark movement). No gravity: the
/// agent holds whatever Y it's placed/patrol-routed at, like a fish holding its depth.
/// </summary>
public partial class DirectTranslationLocomotion : CharacterBody3D, IAgentMover
{
    [Export] public float Speed { get; set; } = 4f;
    [Export] public float RotationSpeed { get; set; } = 6f;
    [Export] public float ArrivalDistance { get; set; } = 0.3f;

    private Vector3? _destination;
    private Vector3? _faceTarget;

    public bool HasArrived => !_destination.HasValue || GlobalPosition.DistanceTo(_destination.Value) < ArrivalDistance;

    public void MoveTo(Vector3 destination)
    {
        _faceTarget = null;
        _destination = destination;
    }

    public void FaceWorldPosition(Vector3 position)
    {
        _faceTarget = position;
    }

    public void Stop()
    {
        _destination = GlobalPosition;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Velocity;

        if (!HasArrived)
        {
            Vector3 toDestination = _destination.Value - GlobalPosition;
            toDestination.Y = 0f;

            if (toDestination.LengthSquared() > 0.0001f)
            {
                Vector3 direction = toDestination.Normalized();
                velocity.X = direction.X * Speed;
                velocity.Z = direction.Z * Speed;
                Basis = LocomotionMath.TurnToward(Basis, direction, RotationSpeed, dt);
            }
        }
        else
        {
            velocity.X = 0f;
            velocity.Z = 0f;

            if (_faceTarget.HasValue)
            {
                Vector3 toTarget = _faceTarget.Value - GlobalPosition;
                toTarget.Y = 0f;
                if (toTarget.LengthSquared() > 0.0001f)
                {
                    Basis = LocomotionMath.TurnToward(Basis, toTarget.Normalized(), RotationSpeed, dt);
                }
            }
        }

        velocity.Y = 0f;
        Velocity = velocity;
        MoveAndSlide();
    }
}
