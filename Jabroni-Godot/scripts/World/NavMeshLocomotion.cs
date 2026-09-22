using Godot;

namespace Jabroni.World;

/// <summary>Shared NavigationAgent3D-driven CharacterBody3D movement for avatar/NPC archetypes.</summary>
public partial class NavMeshLocomotion : CharacterBody3D, IAgentMover
{
    [Export] public float Speed { get; set; } = 5f;
    [Export] public float Gravity { get; set; } = 20f;
    [Export] public float RotationSpeed { get; set; } = 10f;

    /// <summary>
    /// Steepest slope this body treats as floor. Keep it a few degrees ABOVE the navmesh's
    /// agent_max_slope (currently 30) and never far above, because the error is bad in both
    /// directions: let the body climb much steeper than the bake and it walks onto ground with
    /// no navmesh under it and strands itself; let it climb less than the bake and the navmesh
    /// promises slopes the body slides back down, so it presses into them and never arrives.
    /// The small margin favours the second, since a thin off-mesh band is recoverable and a
    /// wedged agent is not. Recast judges slope on a voxelised approximation of the terrain
    /// while the physics engine uses the true contact normal, so the two disagree by a couple
    /// of degrees at the boundary no matter what numbers you write here -- that disagreement is
    /// what the margin is sized to cover.
    /// </summary>
    [Export] public float MaxSlopeDegrees { get; set; } = 33f;

    /// <summary>
    /// How far a re-issued destination has to move before it counts as a new target.
    /// NavigationAgent3D.TargetPosition forces a full repath on every assignment, even
    /// when the value is unchanged -- so a task that re-issues MoveTo each frame
    /// (AITask_ChaseTarget) rebuilds the path before the agent ever advances past its
    /// first point, and the agent stutters in place instead of walking.
    /// </summary>
    [Export] public float RepathThreshold { get; set; } = 0.5f;

    /// <summary>
    /// Seconds without horizontal progress before an unfinished path is treated as
    /// arrived. Where the navmesh disagrees with collision geometry -- a polygon laid
    /// over a wall, say -- the agent presses into the wall and IsNavigationFinished()
    /// never fires, which wedges every FSM transition waiting on HasArrived. This is a
    /// backstop for that, not a substitute for a navmesh that matches the level.
    /// </summary>
    [Export] public float StuckTimeout { get; set; } = 1.5f;

    private const float ProgressEpsilon = 0.05f;

    private NavigationAgent3D _agent;
    private Vector3? _faceTarget;
    private Vector3 _requestedDestination;
    private Vector3 _lastProgressPosition;
    private double _stuckTimer;
    private bool _hasDestination;
    private bool _onNavMesh;
    private bool _gaveUp;

    /// <summary>True when there's nothing left to walk to: no destination, the path
    /// finished, or the agent stopped making progress and gave up. A request still waiting to
    /// be projected onto the navmesh counts as not arrived -- the agent has somewhere to be,
    /// it just doesn't have a route there yet.</summary>
    public bool HasArrived =>
        !_hasDestination || _gaveUp || (_onNavMesh && _agent.IsNavigationFinished());

    public override void _Ready()
    {
        _agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
        _lastProgressPosition = GlobalPosition;
        FloorMaxAngle = Mathf.DegToRad(MaxSlopeDegrees);
    }

    public void MoveTo(Vector3 destination)
    {
        _faceTarget = null;

        // Re-issuing the same destination would cost a repath for nothing.
        if (_hasDestination && !_gaveUp
            && destination.DistanceSquaredTo(_requestedDestination) < RepathThreshold * RepathThreshold)
        {
            return;
        }

        _requestedDestination = destination;
        _hasDestination = true;
        _onNavMesh = false;
        _gaveUp = false;
        _stuckTimer = 0;
        _lastProgressPosition = GlobalPosition;

        ResolveDestination();
    }

    /// <summary>
    /// Projects the pending destination onto the navmesh and hands it to the agent. Nothing
    /// off-mesh is ever submitted: callers pass whatever they computed (an offset from another
    /// agent, an authored waypoint, a chase target's position) and this is the one place that
    /// decides where an agent is allowed to be sent.
    ///
    /// It can legitimately fail on the frame it's asked -- state machines issue their first
    /// move during init, before the navigation map has synchronized -- so it's retried from
    /// _PhysicsProcess rather than falling back to the raw point.
    /// </summary>
    private void ResolveDestination()
    {
        if (!_hasDestination || _onNavMesh)
        {
            return;
        }

        if (!NavMeshSnap.TryProject(_agent.GetNavigationMap(), _requestedDestination, out Vector3 onMesh))
        {
            return;
        }

        _onNavMesh = true;
        _agent.TargetPosition = onMesh;
    }

    public void FaceWorldPosition(Vector3 position)
    {
        _faceTarget = position;
    }

    /// <summary>Cancels any in-flight navigation so the agent holds position (e.g. when a
    /// patrol gets interrupted by a disturbance or a chat request).</summary>
    public void Stop()
    {
        _hasDestination = false;
        _onNavMesh = false;
        _gaveUp = false;
        _stuckTimer = 0;
        _agent.TargetPosition = GlobalPosition;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        ResolveDestination();

        Vector3 velocity = Velocity;
        velocity.Y = IsOnFloor() ? 0f : velocity.Y - Gravity * dt;

        if (!HasArrived)
        {
            Vector3 nextPos = _onNavMesh ? _agent.GetNextPathPosition() : GlobalPosition;
            Vector3 toNext = nextPos - GlobalPosition;
            toNext.Y = 0f;

            if (toNext.LengthSquared() > 0.0001f)
            {
                Vector3 direction = toNext.Normalized();
                velocity.X = direction.X * Speed;
                velocity.Z = direction.Z * Speed;
                Basis = LocomotionMath.TurnToward(Basis, direction, RotationSpeed, dt);
            }

            TrackProgress(dt);
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

        Velocity = velocity;
        MoveAndSlide();
    }

    /// <summary>Gives up on a destination the agent has stopped closing on, so the state
    /// machine isn't left waiting on an arrival that will never come.</summary>
    private void TrackProgress(float dt)
    {
        Vector3 travelled = GlobalPosition - _lastProgressPosition;
        travelled.Y = 0f;

        if (travelled.LengthSquared() > ProgressEpsilon * ProgressEpsilon)
        {
            _lastProgressPosition = GlobalPosition;
            _stuckTimer = 0;
            return;
        }

        _stuckTimer += dt;
        if (_stuckTimer >= StuckTimeout)
        {
            _gaveUp = true;
        }
    }
}
