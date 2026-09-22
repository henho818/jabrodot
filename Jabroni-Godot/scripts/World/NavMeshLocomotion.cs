using Godot;

namespace Jabroni.World;

/// <summary>Shared NavigationAgent3D-driven CharacterBody3D movement for avatar/NPC archetypes.</summary>
public partial class NavMeshLocomotion : CharacterBody3D, IAgentMover
{
    /// <summary>Metres per second the agent walks at. Overwritten at startup by the agent's
    /// BaseSpeed from Agent_Config.txt when that row supplies one, so editing this in the
    /// Inspector only sticks for agents with no configured speed.</summary>
    [Export] public float Speed { get; set; } = 5f;

    /// <summary>Downward acceleration in metres per second squared, applied only while off the
    /// floor. Nothing here jumps, so this exists to keep the body on uneven terrain and to
    /// bring it down off ledges.</summary>
    [Export] public float Gravity { get; set; } = 20f;

    /// <summary>How quickly the body turns to face its heading, in radians per second. Purely
    /// cosmetic -- turning never holds up movement, so the agent walks its path at full speed
    /// while still swinging around.</summary>
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

    [ExportGroup("Step Up")]
    /// <summary>
    /// Tallest lip the body will hop rather than stop dead at. CharacterBody3D has no step
    /// handling of its own -- floor_max_angle and friends govern slopes, not steps -- so
    /// without this a curb, a rock edge or the lip at the foot of a ramp stops the agent
    /// even where the navmesh runs straight over it (Recast bridges anything under
    /// agent_max_climb). Keep it at or above the bake's agent_max_climb so the body can
    /// honour what the navmesh promised.
    /// </summary>
    [Export] public float StepHeight { get; set; } = 0.35f;

    /// <summary>
    /// How far ahead to probe when testing a step. It has to clear the body's own radius:
    /// probe only a frame's worth of motion and the downward test lands on the step's rounded
    /// edge rather than its top face, reporting a ~45 degree surface that the walkability
    /// check then rejects. Measured against this 0.4-radius capsule: a 0.067 probe reads 44.7
    /// degrees, 0.45 reads 0. Only used for testing -- the body is lifted straight up, never
    /// shoved forward by this much.
    /// </summary>
    [Export] public float StepProbeDistance { get; set; } = 0.5f;

    /// <summary>
    /// Seconds the step-up takes to play out. The lift itself is instantaneous, so this is
    /// what turns it into a movement the player can read: the agent visibly gathers itself and
    /// hops the lip instead of gliding up it. Shorter feels brisk, longer feels deliberate and
    /// weighty. At 0 the body just appears on the step, which reads as a glitch rather than a
    /// decision -- worth knowing, but not a setting to ship.
    /// </summary>
    [Export] public float StepHopDuration { get; set; } = 0.5f;

    /// <summary>
    /// How far above the step the hop peaks, as a multiple of the step's own height. At 1.5 the
    /// agent rises half again as far as it needs to, then drops onto the lip -- it reads as a
    /// jump that lands, rather than a ride up to exactly the right height. Values at or below 1
    /// have no apex to fall from and are clamped away.
    /// </summary>
    [Export] public float StepHopPeakScale { get; set; } = 1.5f;

    [ExportGroup("Pathing")]
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

    /// <summary>Treated as blocked when a frame achieves less than this share of the
    /// horizontal motion it asked for.</summary>
    private const float BlockedFraction = 0.5f;

    /// <summary>Extra downward reach when probing for the top of a step, so the landing is
    /// found rather than the body being left hanging a hair above it.</summary>
    private const float StepProbeMargin = 0.05f;

    /// <summary>Below this the "step" is just the floor we're already on, which is what the
    /// downward probe finds on open ground.</summary>
    private const float MinStepRise = 0.02f;

    /// <summary>Lifted a little past StepHeight before probing forward. Rising exactly the
    /// step's height leaves the capsule grazing its top face, and the forward test then
    /// collides on the solver's safe margin -- a step of precisely StepHeight would be
    /// refused. The rise is still capped at StepHeight afterwards.</summary>
    private const float StepClearance = 0.05f;

    private NavigationAgent3D _agent;
    private Vector3? _faceTarget;
    private Vector3 _requestedDestination;
    private Vector3 _lastProgressPosition;
    private double _stuckTimer;
    private bool _hasDestination;
    private bool _hopping;
    private double _hopElapsed;
    private Vector3 _hopFrom;
    private Vector3 _hopTo;
    private float _hopRise;
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

        // A step-up in flight owns the body outright: no steering, no gravity, no
        // MoveAndSlide, just the scripted arc.
        if (_hopping)
        {
            AdvanceHop(dt);
            return;
        }

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

        Vector3 positionBefore = GlobalPosition;
        MoveAndSlide();

        Vector3 planned = new Vector3(velocity.X, 0f, velocity.Z) * dt;
        Vector3 achieved = GlobalPosition - positionBefore;
        achieved.Y = 0f;

        // Only worth probing when the frame actually wanted to go somewhere and didn't.
        if (StepHeight > 0f
            && IsOnFloor()
            && planned.LengthSquared() > 0.000001f
            && achieved.LengthSquared() < planned.LengthSquared() * BlockedFraction * BlockedFraction)
        {
            TryStepUp(planned);
        }
    }

    /// <summary>
    /// Lifts the body onto a low obstruction it just failed to walk through, by testing the
    /// move a walker makes without thinking about it: up over the lip, forward, then back
    /// down onto whatever is there.
    ///
    /// Every leg is a TestMove first, so a failed attempt costs nothing and leaves the body
    /// exactly where MoveAndSlide put it. The landing has to be real ground and shallow
    /// enough to stand on, otherwise this would happily post the agent up onto a wall the
    /// navmesh never claimed was walkable.
    /// </summary>
    private bool TryStepUp(Vector3 motion)
    {
        Transform3D from = GlobalTransform;
        Vector3 lift = Vector3.Up * (StepHeight + StepClearance);

        // Headroom to rise into.
        if (TestMove(from, lift))
        {
            return false;
        }

        var raised = new Transform3D(from.Basis, from.Origin + lift);

        // Clear passage forward once raised. This is the leg that fails on a real wall.
        Vector3 probe = motion.Normalized() * Mathf.Max(motion.Length(), StepProbeDistance);
        if (TestMove(raised, probe))
        {
            return false;
        }

        // Something to come down onto -- no hit means we'd be stepping into open air.
        var ahead = new Transform3D(raised.Basis, raised.Origin + probe);
        var landing = new KinematicCollision3D();
        if (!TestMove(ahead, Vector3.Down * (StepHeight + StepClearance + StepProbeMargin), landing))
        {
            return false;
        }

        // Refuse anything the body wouldn't accept as floor, so this can't post the agent up
        // onto a wall the navmesh never claimed was walkable.
        if (landing.GetNormal().AngleTo(Vector3.Up) > FloorMaxAngle)
        {
            return false;
        }

        Vector3 landingPoint = ahead.Origin + landing.GetTravel();
        float rise = landingPoint.Y - from.Origin.Y;
        if (rise < MinStepRise || rise > StepHeight)
        {
            // Either the floor we're already standing on, or a step taller than we allow.
            return false;
        }

        if (StepHopDuration <= 0f)
        {
            // Nothing to animate, so don't shove the body forward -- lift it and let its own
            // momentum carry it over the lip next frame.
            GlobalPosition = from.Origin + Vector3.Up * rise;
            return true;
        }

        // Travel to where the probe actually found footing: up and *forward*, clear of the
        // lip. Hopping straight up instead would strand the body over the edge and leave the
        // next frame to slide it forward, which reads as an elevator rather than a step.
        _hopFrom = from.Origin;
        _hopTo = landingPoint;
        _hopRise = rise;
        _hopElapsed = 0;
        _hopping = true;
        return true;
    }

    /// <summary>Plays the step-up out as a visible arc. Lands on exactly the position the
    /// instant version would have snapped to, so the arc changes only what's seen.</summary>
    private void AdvanceHop(float dt)
    {
        _hopElapsed += dt;
        float t = Mathf.Clamp((float)(_hopElapsed / StepHopDuration), 0f, 1f);

        // Horizontal travel is even; height follows a thrown arc. Solving
        //   y(t) = v*t - g*t^2/2,  y(1) = rise,  max(y) = peak * rise
        // gives v = rise * (2k + 2*sqrt(k^2 - k)) and g = v^2 / (2*k*rise), so the apex lands
        // inside the hop instead of after it -- which is what makes the body fall onto the step.
        float k = Mathf.Max(StepHopPeakScale, 1.01f);
        float v = _hopRise * (2f * k + 2f * Mathf.Sqrt(k * k - k));
        float g = v * v / (2f * k * _hopRise);

        Vector3 position = _hopFrom.Lerp(_hopTo, t);
        position.Y = _hopFrom.Y + (v * t) - (0.5f * g * t * t);
        GlobalPosition = position;
        Velocity = Vector3.Zero;

        if (t < 1f)
        {
            return;
        }

        GlobalPosition = _hopTo;
        _hopping = false;

        // The hop is progress, not a stall -- don't let it count toward giving up.
        _lastProgressPosition = GlobalPosition;
        _stuckTimer = 0;
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
