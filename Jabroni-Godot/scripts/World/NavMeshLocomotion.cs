using Godot;

namespace Jabroni.World;

/// <summary>Shared NavigationAgent3D-driven CharacterBody3D movement for avatar/NPC archetypes.</summary>
public partial class NavMeshLocomotion : CharacterBody3D
{
    [Export] public float Speed { get; set; } = 5f;
    [Export] public float Gravity { get; set; } = 20f;
    [Export] public float RotationSpeed { get; set; } = 10f;

    private NavigationAgent3D _agent;
    private Vector3? _faceTarget;

    public bool HasArrived => _agent.IsNavigationFinished();

    public override void _Ready()
    {
        _agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
    }

    public void MoveTo(Vector3 destination)
    {
        _faceTarget = null;
        _agent.TargetPosition = destination;
    }

    public void FaceWorldPosition(Vector3 position)
    {
        _faceTarget = position;
    }

    /// <summary>Cancels any in-flight navigation so the agent holds position (e.g. when a
    /// patrol gets interrupted by a disturbance or a chat request).</summary>
    public void Stop()
    {
        _agent.TargetPosition = GlobalPosition;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Velocity;
        velocity.Y = IsOnFloor() ? 0f : velocity.Y - Gravity * dt;

        if (!HasArrived)
        {
            Vector3 nextPos = _agent.GetNextPathPosition();
            Vector3 toNext = nextPos - GlobalPosition;
            toNext.Y = 0f;

            if (toNext.LengthSquared() > 0.0001f)
            {
                Vector3 direction = toNext.Normalized();
                velocity.X = direction.X * Speed;
                velocity.Z = direction.Z * Speed;
                TurnToward(direction, dt);
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
                    TurnToward(toTarget.Normalized(), dt);
                }
            }
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private void TurnToward(Vector3 direction, float dt)
    {
        Basis targetBasis = Basis.LookingAt(direction, Vector3.Up);
        Basis = Basis.Slerp(targetBasis, RotationSpeed * dt);
    }
}
