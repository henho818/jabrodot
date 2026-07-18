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
	public Node3D ChatTarget { get; set; }
	public Vector3? DisturbancePosition { get; private set; }
	public double LastDisturbanceTime { get; private set; } = double.NegativeInfinity;
	public Node3D AttackTarget { get; private set; }
	public double LastTargetAcquiredTime { get; private set; } = double.NegativeInfinity;

	public AIState CurrentState => _stateMachine?.CurrentState ?? AIState.None;

	protected abstract string ConfigId { get; }
	protected virtual bool SnapPatrolPathToTerrain => true;

	private const int MaxPatrolPathSnapAttempts = 120; // ~2s at 60 physics ticks/sec; Terrain3D collision can take a few frames to come online

	private const string PatrolPathProperty = "PatrolPath";

	private AIStateMachine _stateMachine;
	private Label3D _debugLabel;
	private int _patrolPathSnapAttempts;

	/// <summary>
	/// The NavPath this agent patrols, dragged in directly in the Inspector -- no by-name
	/// child lookup, no waiting for _Ready to resolve it. Stored as a NodePath in metadata
	/// rather than an [Export] field: C# exported-property overrides (including Node
	/// references) don't apply from .tscn in this Godot build, but metadata does -- see
	/// ClickToMove's node-wiring comment for the verified platform bug. Resolved on demand
	/// rather than cached, since the assignment can change while editing.
	/// </summary>
	public NavPath PatrolPath => HasMeta(PatrolPathProperty) ? GetNodeOrNull<NavPath>(GetMeta(PatrolPathProperty).AsNodePath()) : null;

	/// <summary>True once PatrolPath's waypoints have been snapped to the terrain (or immediately, for agents that don't snap) -- see _PhysicsProcess.</summary>
	public bool PatrolPathReady { get; private set; }

	public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList()
	{
		return new Godot.Collections.Array<Godot.Collections.Dictionary>
		{
			new()
			{
				{ "name", PatrolPathProperty },
				{ "type", (int)Variant.Type.Object },
				{ "hint", (int)PropertyHint.NodeType },
				{ "hint_string", nameof(NavPath) },
				{ "usage", (int)PropertyUsageFlags.Editor },
			},
		};
	}

	public override Variant _Get(StringName property)
	{
		if (property == PatrolPathProperty)
		{
			return PatrolPath;
		}

		return default;
	}

	public override bool _Set(StringName property, Variant value)
	{
		if (property == PatrolPathProperty)
		{
			if (value.AsGodotObject() is Node node)
			{
				SetMeta(PatrolPathProperty, GetPathTo(node));
			}
			else
			{
				RemoveMeta(PatrolPathProperty);
			}

			return true;
		}

		return false;
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
