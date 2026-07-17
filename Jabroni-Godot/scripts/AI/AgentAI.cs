using Godot;
using Jabroni.Data;
using Jabroni.Nav;
using Jabroni.World;

namespace Jabroni.AI;

/// <summary>
/// Per-agent runtime AI state: owns the state machine and the data its tasks/conditions
/// read (patrol path, chat target, disturbance/vision info). Attach a concrete subclass
/// (e.g. NpcAgentAI) as a child of the agent's body node.
/// </summary>
public abstract partial class AgentAI : Node
{
	public Node3D Body { get; private set; }
	public IAgentMover Locomotion { get; private set; }
	public AgentStats Stats { get; private set; }
	public NavPath PatrolPath { get; set; }
	public Node3D ChatTarget { get; set; }
	public Vector3? DisturbancePosition { get; private set; }
	public double LastDisturbanceTime { get; private set; } = double.NegativeInfinity;
	public Node3D AttackTarget { get; private set; }
	public double LastTargetAcquiredTime { get; private set; } = double.NegativeInfinity;

	public AIState CurrentState => _stateMachine?.CurrentState ?? AIState.None;

	protected abstract string ConfigId { get; }
	protected virtual double DefaultPatrolStayDuration => 1.5;
	protected virtual bool SnapPatrolPathToTerrain => true;

	private const int MaxPatrolPathBuildAttempts = 120; // ~2s at 60 physics ticks/sec; Terrain3D collision can take a few frames to come online

	private AIStateMachine _stateMachine;
	private Label3D _debugLabel;
	private int _patrolPathBuildAttempts;

	public override void _Ready()
	{
		Body = GetParent<Node3D>();
		Locomotion = Body as IAgentMover;
		_debugLabel = Body.GetNodeOrNull<Label3D>("DebugStateLabel");

		var configRepo = GetNode<AgentConfigRepository>("/root/AgentConfigRepository");
		var row = configRepo.Get(ConfigId);
		Stats = row != null ? new AgentStats(row) : AgentStats.Empty;

		if (Locomotion != null && Stats.BaseSpeed > 0f)
		{
			Locomotion.Speed = Stats.BaseSpeed;
		}

		var detectionSphere = Body.GetNodeOrNull<AgentDetectionSphere>("DetectionSphere");
		detectionSphere?.Initialize(this, Stats.DetectionRadius);

		var vision = Body.GetNodeOrNull<AgentVision>("Vision");
		vision?.Initialize(this, Stats.DetectionRadius * 0.6f);

		_stateMachine = CreateStateMachine();
		_stateMachine.Init();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (PatrolPath == null && _patrolPathBuildAttempts < MaxPatrolPathBuildAttempts)
		{
			_patrolPathBuildAttempts++;
			PatrolPath = PatrolPathBuilder.Build(Body, "PatrolPath", DefaultPatrolStayDuration, looping: true, SnapPatrolPathToTerrain);
		}
	}

	public override void _Process(double delta)
	{
		_stateMachine.Update(delta);

		if (_debugLabel != null)
		{
			_debugLabel.Text = CurrentState.ToString();
		}
	}

	public void ReportDisturbance(Vector3 position)
	{
		DisturbancePosition = position;
		LastDisturbanceTime = Time.GetTicksMsec() / 1000.0;
	}

	public void ReportTargetSighted(Node3D target)
	{
		AttackTarget = target;
		LastTargetAcquiredTime = Time.GetTicksMsec() / 1000.0;
	}

	public void ClearAttackTarget()
	{
		AttackTarget = null;
	}

	protected abstract AIStateMachine CreateStateMachine();
}
