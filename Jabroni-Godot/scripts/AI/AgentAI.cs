using System;
using Godot;
using Jabroni.Data;
using Jabroni.Nav;
using Jabroni.World;

namespace Jabroni.AI;

/// <summary>
/// Per-agent runtime AI state: owns the state machine and the data its tasks/conditions
/// read (patrol path, chat target, disturbance/vision info). Attach a concrete subclass
/// (e.g. NpcAgentAI) as a child of the agent's body node.
///
/// Every concrete subclass must carry its own [Tool] attribute so the editor calls into
/// _GetPropertyList/_Get for the debug fields below -- Godot's C# tool-attribute check
/// (ScriptManagerBridge.GetScriptTypeInfo) reflects the exact attached script type with
/// inherit: false, so [Tool] on this abstract base would be silently ignored.
/// _Ready/_Process/_PhysicsProcess all bail out via Engine.IsEditorHint() since none of
/// that runtime setup (autoload lookup, state machine, terrain snapping) is meaningful or
/// safe outside Play.
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
	private const string BodyProperty = "Body";
	private const string LocomotionProperty = "Locomotion";
	private const string StatsProperty = "Stats";
	private const string ChatTargetProperty = "ChatTarget";
	private const string DisturbancePositionProperty = "DisturbancePosition";
	private const string LastDisturbanceTimeProperty = "LastDisturbanceTime";
	private const string AttackTargetProperty = "AttackTarget";
	private const string LastTargetAcquiredTimeProperty = "LastTargetAcquiredTime";
	private const string CurrentStateProperty = "CurrentState";
	private const string PatrolPathReadyProperty = "PatrolPathReady";

	private static readonly string AiStateHintString = string.Join(",", Enum.GetNames(typeof(AIState)));

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
			new()
			{
				{ "name", "AI State (Read-Only)" },
				{ "type", (int)Variant.Type.Nil },
				{ "usage", (int)PropertyUsageFlags.Group },
			},
			ReadOnlyObjectProperty(BodyProperty, nameof(Node3D)),
			ReadOnlyObjectProperty(LocomotionProperty, nameof(Node)),
			ReadOnlyProperty(StatsProperty, Variant.Type.String),
			ReadOnlyObjectProperty(ChatTargetProperty, nameof(Node3D)),
			ReadOnlyProperty(DisturbancePositionProperty, Variant.Type.Vector3),
			ReadOnlyProperty(LastDisturbanceTimeProperty, Variant.Type.Float),
			ReadOnlyObjectProperty(AttackTargetProperty, nameof(Node3D)),
			ReadOnlyProperty(LastTargetAcquiredTimeProperty, Variant.Type.Float),
			new()
			{
				{ "name", CurrentStateProperty },
				{ "type", (int)Variant.Type.Int },
				{ "hint", (int)PropertyHint.Enum },
				{ "hint_string", AiStateHintString },
				{ "usage", (int)(PropertyUsageFlags.Editor | PropertyUsageFlags.ReadOnly) },
			},
			ReadOnlyProperty(PatrolPathReadyProperty, Variant.Type.Bool),
		};
	}

	private static Godot.Collections.Dictionary ReadOnlyProperty(string name, Variant.Type type)
	{
		return new Godot.Collections.Dictionary
		{
			{ "name", name },
			{ "type", (int)type },
			{ "usage", (int)(PropertyUsageFlags.Editor | PropertyUsageFlags.ReadOnly) },
		};
	}

	private static Godot.Collections.Dictionary ReadOnlyObjectProperty(string name, string hintString)
	{
		return new Godot.Collections.Dictionary
		{
			{ "name", name },
			{ "type", (int)Variant.Type.Object },
			{ "hint", (int)PropertyHint.NodeType },
			{ "hint_string", hintString },
			{ "usage", (int)(PropertyUsageFlags.Editor | PropertyUsageFlags.ReadOnly) },
		};
	}

	public override Variant _Get(StringName property)
	{
		if (property == PatrolPathProperty)
		{
			return PatrolPath;
		}

		if (property == BodyProperty)
		{
			return Body;
		}

		if (property == LocomotionProperty)
		{
			return Locomotion as Node;
		}

		if (property == StatsProperty)
		{
			return FormatStats(Stats);
		}

		if (property == ChatTargetProperty)
		{
			return ChatTarget;
		}

		if (property == DisturbancePositionProperty)
		{
			return DisturbancePosition ?? default;
		}

		if (property == LastDisturbanceTimeProperty)
		{
			return LastDisturbanceTime;
		}

		if (property == AttackTargetProperty)
		{
			return AttackTarget;
		}

		if (property == LastTargetAcquiredTimeProperty)
		{
			return LastTargetAcquiredTime;
		}

		if (property == CurrentStateProperty)
		{
			return (int)CurrentState;
		}

		if (property == PatrolPathReadyProperty)
		{
			return PatrolPathReady;
		}

		return default;
	}

	private static string FormatStats(AgentStats stats)
	{
		return stats == null
			? "(unset)"
			: $"{stats.Name} | Speed={stats.BaseSpeed} AttackDist={stats.AttackDistance} AlertDisengage={stats.AlertDisengageTime} SearchDisengage={stats.SearchDisengageTime} DetectionRadius={stats.DetectionRadius} Dialog={stats.ChatDialogId}";
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
		if (Engine.IsEditorHint())
		{
			return;
		}

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
		if (Engine.IsEditorHint())
		{
			return;
		}

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
		if (Engine.IsEditorHint())
		{
			return;
		}

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
