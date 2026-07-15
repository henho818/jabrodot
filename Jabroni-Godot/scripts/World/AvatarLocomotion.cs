using Godot;

namespace Jabroni.World;

public partial class AvatarLocomotion : CharacterBody3D
{
    [Export] public float Speed { get; set; } = 5f;
    [Export] public float Gravity { get; set; } = 20f;
    [Export] public float RotationSpeed { get; set; } = 10f;

    private NavigationAgent3D _agent;
    private Node3D _faceTargetOnArrival;

    public override void _Ready()
    {
        _agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
    }

    /// <summary>Walks to destination; once arrived, turns to face faceOnArrival if given (e.g. an NPC being approached).</summary>
    public void MoveTo(Vector3 destination, Node3D faceOnArrival = null)
    {
        _agent.TargetPosition = destination;
        _faceTargetOnArrival = faceOnArrival;
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 velocity = Velocity;
        float dt = (float)delta;

        velocity.Y = IsOnFloor() ? 0f : velocity.Y - Gravity * dt;

        if (!_agent.IsNavigationFinished())
        {
            Vector3 nextPos = _agent.GetNextPathPosition();
            Vector3 toNext = nextPos - GlobalPosition;
            toNext.Y = 0f;

            if (toNext.LengthSquared() > 0.0001f)
            {
                Vector3 direction = toNext.Normalized();
                velocity.X = direction.X * Speed;
                velocity.Z = direction.Z * Speed;

                Basis targetBasis = Basis.LookingAt(direction, Vector3.Up);
                Basis = Basis.Slerp(targetBasis, RotationSpeed * dt);
            }
        }
        else
        {
            velocity.X = 0f;
            velocity.Z = 0f;
            FaceArrivalTargetIfAny(dt);
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private void FaceArrivalTargetIfAny(float dt)
    {
        if (_faceTargetOnArrival == null)
        {
            return;
        }

        Vector3 toTarget = _faceTargetOnArrival.GlobalPosition - GlobalPosition;
        toTarget.Y = 0f;
        if (toTarget.LengthSquared() <= 0.0001f)
        {
            return;
        }

        Basis targetBasis = Basis.LookingAt(toTarget.Normalized(), Vector3.Up);
        Basis = Basis.Slerp(targetBasis, RotationSpeed * dt);
    }
}
