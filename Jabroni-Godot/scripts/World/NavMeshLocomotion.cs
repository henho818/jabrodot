using Godot;

namespace Jabroni.World;

/// <summary>Shared NavigationAgent3D-driven CharacterBody3D movement for avatar/NPC archetypes.</summary>
[Tool]
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

    /// <summary>
    /// Tallest lip the body will hop rather than stop dead at. CharacterBody3D has no step
    /// handling of its own -- floor_max_angle and friends govern slopes, not steps -- so
    /// without this a curb, a rock edge or the lip at the foot of a ramp stops the agent
    /// even where the navmesh runs straight over it (Recast bridges anything under
    /// agent_max_climb). Keep it at or above the bake's agent_max_climb so the body can
    /// honour what the navmesh promised.
    /// </summary>
    [ExportGroup("Step Up")]
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
    /// Deepest drop the agent hops down rather than just walking off the edge. <b>0 disables
    /// hopping down entirely</b>, which is the default: falling off a ledge already reads
    /// fine, while every hop costs a plant plus an arc -- so on a flight of steps the agent
    /// stops to consider each one and the descent looks like dithering rather than intent.
    /// Climbing is the half worth animating, because that's where the body would otherwise
    /// stop dead against the lip.
    ///
    /// Raise it to turn descents back on; it's the drop height in metres, and anything deeper
    /// is walked off and left to gravity either way.
    /// </summary>
    [Export] public float StepDownHeight { get; set; }

    /// <summary>
    /// How far ahead the descent lands. It has to be longer than the climb's probe: to drop,
    /// the capsule must clear the ledge it is leaving *behind* it, not merely get its leading
    /// edge over. Measured on a 0.4-radius capsule, 0.5 still overlapped the old surface and
    /// read the drop as zero; 1.0 measured it exactly.
    /// </summary>
    [Export] public float StepDownProbeDistance { get; set; } = 1f;

    /// <summary>
    /// Seconds the step-up takes to play out. The lift itself is instantaneous, so this is
    /// what turns it into a movement the player can read: the agent visibly gathers itself and
    /// hops the lip instead of gliding up it. Shorter feels brisk, longer feels deliberate and
    /// weighty. At 0 the body just appears on the step, which reads as a glitch rather than a
    /// decision -- worth knowing, but not a setting to ship.
    /// </summary>
    [Export] public float StepHopDuration { get; set; } = 0.5f;

    /// <summary>
    /// How high the arc rises above the midpoint of the hop, as a multiple of the height
    /// difference between where it starts and where it lands. At 1 the apex sits over the
    /// halfway point, one full height-delta above it -- so a climb and a drop of the same size
    /// trace the same shape, and dropping doesn't fling the agent upward the way measuring the
    /// apex from the start did.
    /// </summary>
    [Export] public float StepHopPeakScale { get; set; } = 1f;

    /// <summary>
    /// How near the body must be facing the hop before it launches, in degrees. The agent
    /// plants, squares up to the lip and only then jumps, so the hop always goes the way it's
    /// looking rather than being launched sideways mid-stride. Widen it for a looser, faster
    /// wind-up; narrow it to make the agent commit to the turn.
    /// </summary>
    [Export] public float StepTurnTolerance { get; set; } = 5f;

    /// <summary>
    /// Seconds the agent plants before launching, even when it's already facing the right way.
    /// Agents rotate to face their direction of travel as they walk and the hop goes that same
    /// way, so the turn is usually satisfied on the first frame -- without a held beat there'd
    /// be no stop to see, and the jump would read as a stumble mid-stride. This is the pause
    /// that makes it look chosen.
    /// </summary>
    [Export] public float StepPlantDuration { get; set; } = 0.25f;

    /// <summary>
    /// How far a re-issued destination has to move before it counts as a new target.
    /// NavigationAgent3D.TargetPosition forces a full repath on every assignment, even
    /// when the value is unchanged -- so a task that re-issues MoveTo each frame
    /// (AITask_ChaseTarget) rebuilds the path before the agent ever advances past its
    /// first point, and the agent stutters in place instead of walking.
    /// </summary>
    [ExportGroup("Pathing")]
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

    /// <summary>Caps the wind-up so a body that somehow never settles on its heading can't
    /// stand there turning forever.</summary>
    private const float StepTurnTimeout = 1f;

    /// <summary>How far past a max-slope ramp's drop a descent has to fall before it counts as
    /// a ledge worth hopping, rather than ground the agent can just walk down.</summary>
    private const float LedgeMargin = 0.02f;

    /// <summary>Spacing of the ground samples used to spot a ledge. Small, because the whole
    /// point is to catch a drop that happens between two samples rather than across the
    /// stride: over a long span a ledge and a ramp look identical.</summary>
    private const float LedgeSampleSpacing = 0.1f;

    private const int LedgeSampleCount = 14;

    private NavigationAgent3D _agent;
    private Vector3? _faceTarget;
    private Vector3 _requestedDestination;
    private Vector3 _lastProgressPosition;
    private double _stuckTimer;
    private bool _hasDestination;
    /// <summary>Plant and square up to the lip, then jump it.</summary>
    private enum StepPhase
    {
        None,
        Turning,
        Hopping,
    }

    private StepPhase _stepPhase;
    private double _turnElapsed;
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

        // A step-up in flight owns the body outright -- no path steering until it's done.
        if (_stepPhase == StepPhase.Turning)
        {
            AdvanceStepTurn(dt);
            return;
        }

        if (_stepPhase == StepPhase.Hopping)
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
        if (IsOnFloor() && planned.LengthSquared() > 0.000001f)
        {
            bool blocked = achieved.LengthSquared()
                < planned.LengthSquared() * BlockedFraction * BlockedFraction;

            // Being stopped means something is in the way to climb; getting through freely
            // means the only step available is one down off an edge ahead.
            if (blocked)
            {
                TryStepUp(planned);
            }
            else
            {
                TryStepDown(planned);
            }
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
        if (StepHeight <= 0f)
        {
            return false;
        }

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
        _turnElapsed = 0;
        _stepPhase = StepPhase.Turning;
        return true;
    }

    /// <summary>
    /// Hops down off a ledge the agent is about to walk over. Nothing obstructs a descent, so
    /// unlike a step up there's no blocked frame to react to -- the drop has to be looked for.
    /// A ramp within the body's slope limit also loses height over the same distance, so only a
    /// fall steeper than that counts, which keeps this off ordinary sloping ground.
    /// </summary>
    private bool TryStepDown(Vector3 motion)
    {
        if (StepDownHeight <= 0f)
        {
            return false;
        }

        Vector3 direction = motion.Normalized();
        Transform3D from = GlobalTransform;

        if (!HasLedgeAhead(from.Origin, direction, out float toLedge, out float groundHere, out float groundBelow))
        {
            return false;
        }

        float drop = groundHere - groundBelow;
        if (drop < MinStepRise || drop > StepDownHeight)
        {
            return false;
        }

        // Land clear of the ledge by the capsule's own reach, at the height the rays found.
        // Deriving it this way rather than from a capsule probe matters: the capsule only
        // measures a drop once it has cleared the edge, by which point the body is already
        // falling and the hop never gets a chance to fire.
        float forward = toLedge + StepDownProbeDistance;
        var lifted = new Transform3D(from.Basis, from.Origin + (Vector3.Up * StepClearance));

        // Nothing may block the way over.
        if (TestMove(lifted, direction * forward))
        {
            return false;
        }

        Vector3 landingPoint = from.Origin + (direction * forward);
        landingPoint.Y = groundBelow + (from.Origin.Y - groundHere);

        _hopFrom = from.Origin;
        _hopTo = landingPoint;
        _hopRise = -drop;
        _turnElapsed = 0;
        _stepPhase = StepPhase.Turning;
        return true;
    }

    /// <summary>
    /// Walks a line of thin downward rays ahead of the body looking for a break in the ground.
    /// Rays rather than the capsule, because the capsule's own width smears the edge out and
    /// reports no drop at all; and sample by sample, because a ledge only looks different from
    /// a walkable ramp over a short span -- measured across a whole stride the two are the
    /// same shape.
    /// </summary>
    private bool HasLedgeAhead(
        Vector3 origin, Vector3 direction, out float distance, out float groundHere, out float groundBelow)
    {
        distance = 0f;
        groundHere = 0f;
        groundBelow = 0f;

        var space = GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { GetRid() };
        float rampDrop = (LedgeSampleSpacing * Mathf.Tan(FloorMaxAngle)) + LedgeMargin;

        float previous = 0f;
        bool havePrevious = false;

        for (int i = 0; i <= LedgeSampleCount; i++)
        {
            float along = LedgeSampleSpacing * i;
            Vector3 at = origin + (direction * along);
            var query = PhysicsRayQueryParameters3D.Create(
                at + (Vector3.Up * 0.1f),
                at + (Vector3.Down * (StepDownHeight + 1f)),
                CollisionMask);
            query.Exclude = exclude;

            var hit = space.IntersectRay(query);
            if (hit.Count == 0)
            {
                // Open air below: a genuine fall, not something to hop down.
                return false;
            }

            float groundY = ((Vector3)hit["position"]).Y;

            if (i == 0)
            {
                groundHere = groundY;
            }
            else if (havePrevious && previous - groundY > rampDrop)
            {
                distance = along;
                groundBelow = groundY;
                return true;
            }

            previous = groundY;
            havePrevious = true;
        }

        return false;
    }

    /// <summary>
    /// Holds the body still and turns it to face the lip before the jump. Walking into a step
    /// and hopping it in one motion reads as a stumble; stopping to square up makes the jump
    /// look like something the agent decided to do.
    /// </summary>
    private void AdvanceStepTurn(float dt)
    {
        _turnElapsed += dt;

        Vector3 heading = _hopTo - _hopFrom;
        heading.Y = 0f;

        if (heading.LengthSquared() < 0.0001f)
        {
            BeginHop();
            return;
        }

        Vector3 desired = heading.Normalized();

        // Full stop, still subject to gravity so the body stays planted while it turns.
        Velocity = new Vector3(0f, IsOnFloor() ? 0f : Velocity.Y - Gravity * dt, 0f);
        MoveAndSlide();

        Basis = LocomotionMath.TurnToward(Basis, desired, RotationSpeed, dt);

        // Give up on squaring up rather than stand here turning forever.
        if (_turnElapsed >= StepTurnTimeout)
        {
            BeginHop();
            return;
        }

        // Hold the plant even once aimed, so there's a beat to see.
        if (_turnElapsed < StepPlantDuration)
        {
            return;
        }

        // Basis.LookingAt aims -Z down the heading, so that's the axis to measure.
        float facing = (-Basis.Z).Normalized().Dot(desired);
        if (facing >= Mathf.Cos(Mathf.DegToRad(StepTurnTolerance)))
        {
            BeginHop();
        }
    }

    /// <summary>Launches the arc from wherever the wind-up left the body.</summary>
    private void BeginHop()
    {
        _hopFrom = GlobalPosition;
        _hopRise = _hopTo.Y - _hopFrom.Y;
        _hopElapsed = 0;

        if (Mathf.Abs(_hopRise) < MinStepRise)
        {
            // Settled level with the step while turning, so there's nothing to jump.
            GlobalPosition = _hopTo;
            EndStep();
            return;
        }

        _stepPhase = StepPhase.Hopping;
    }

    private void EndStep()
    {
        _stepPhase = StepPhase.None;

        // The step is progress, not a stall -- don't let it count toward giving up.
        _lastProgressPosition = GlobalPosition;
        _stuckTimer = 0;
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
        // Apex over the midpoint, a height-delta above it. Stated as a bump on top of the
        // straight line between the two points, because no parabola through two points at
        // different heights can put its own maximum at their midpoint.
        float delta = Mathf.Abs(_hopTo.Y - _hopFrom.Y);
        float bump = Mathf.Sin(t * Mathf.Pi) * delta * Mathf.Max(StepHopPeakScale, 0f);

        Vector3 position = _hopFrom.Lerp(_hopTo, t);
        position.Y += bump;
        GlobalPosition = position;
        Velocity = Vector3.Zero;

        if (t < 1f)
        {
            return;
        }

        GlobalPosition = _hopTo;
        EndStep();
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
