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
	/// <summary>
	/// The NavPath this agent patrols, dragged in directly in the Inspector. Public setter
	/// so a different NavPath can be dragged in while the agent is running, to test dynamic
	/// path-switching -- see PatrolPathReady for forcing a re-snap after doing so.
	/// </summary>
	[ExportGroup("Patrol")]
	[Export]
	public NavPath PatrolPath { get; set; }

	/// <summary>True once PatrolPath's waypoints have been snapped to the terrain (or immediately, for agents that don't snap) -- see _PhysicsProcess. Settable so a swapped PatrolPath can be forced to re-snap.</summary>
	[Export]
	public bool PatrolPathReady { get; set; }

	[ExportGroup("References")]
	[Export]
	public Node3D Body { get; set; }

	/// <summary>Live pass-through to Locomotion: dragging a different node in reassigns Locomotion for real, so movement systems can be swapped live.</summary>
	[Export]
	public Node LocomotionNode
	{
		get => Locomotion as Node;
		set => Locomotion = value as IAgentMover;
	}

	/// <summary>Read-only display; [Export] requires a setter, but there's nothing meaningful to write back onto Stats from a formatted string.</summary>
	[Export]
	public string StatsDisplay
	{
		get => FormatStats(Stats);
		set => _ = value;
	}

	[ExportGroup("AI State")]
	/// <summary>
	/// Setting this from the Inspector actually drives the state machine (via
	/// AIStateMachine.ForceState), not just a cosmetic value that gets overwritten next
	/// frame -- lets you force a transition mid-Play to test how the agent responds.
	/// </summary>
	[Export]
	public AIState CurrentState
	{
		get => _currentState;
		set
		{
			_currentState = value;
			_stateMachine?.ForceState(value);
		}
	}
	
	[Export]
	public Node3D ChatTarget { get; set; }

	[Export]
	public Vector3 DisturbancePosition { get; set; }

	[Export]
	public double LastDisturbanceTime { get; set; } = double.NegativeInfinity;

	[Export]
	public Node3D AttackTarget { get; set; }

	[Export]
	public double LastTargetAcquiredTime { get; set; } = double.NegativeInfinity;

	public IAgentMover Locomotion { get; private set; }
	public AgentStats Stats { get; private set; }

	protected abstract string ConfigId { get; }
	protected virtual bool SnapPatrolPathToTerrain => true;

	private const int MaxPatrolPathSnapAttempts = 120; // ~2s at 60 physics ticks/sec; Terrain3D collision can take a few frames to come online

	private AIStateMachine _stateMachine;
	private Label3D _debugLabel;
	private int _patrolPathSnapAttempts;
	private AIState _currentState;

	private static string FormatStats(AgentStats stats)
	{
		return stats == null
			? "(unset)"
			: $"{stats.Name} | Speed={stats.BaseSpeed} AttackDist={stats.AttackDistance} AlertDisengage={stats.AlertDisengageTime} SearchDisengage={stats.SearchDisengageTime} DetectionRadius={stats.DetectionRadius} Dialog={stats.ChatDialogId}";
	}

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
		if (!PatrolPathReady && PatrolPath != null && _patrolPathSnapAttempts < MaxPatrolPathSnapAttempts)
		{
			_patrolPathSnapAttempts++;

			if (!SnapPatrolPathToTerrain || NavPathTerrainSnapper.TrySnap(PatrolPath))
			{
				PatrolPathReady = true;
			}
		}
	}

	public override void _Process(double delta)
	{
		_stateMachine.Update(delta);
		_currentState = _stateMachine.CurrentState;

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
