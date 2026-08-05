using System.Collections.Generic;
using Godot;
using Jabroni.AI;
using Jabroni.Triggers.Handlers;

namespace Jabroni.Triggers;

/// <summary>
/// A volume in the world that fires an event when something walks into (or out of) it: drop a
/// collider in the scene, attach this script to it, and pick what the trigger is for from the Type
/// dropdown. Type is also what the trigger *does* -- choosing Dialogue reveals a Dialog picker
/// right underneath it, so the common case (walk into this box, this conversation opens) needs no
/// second node.
/// <para>
/// The built-in behaviours are the ones with a system behind them already: Dialogue opens the
/// dialog box on <see cref="ChatDialogId"/>, the walk-into-it counterpart to AITask_TriggerDialog,
/// which is how clicking an NPC opens the same box; Scene Load swaps the scene for
/// <see cref="ScenePath"/>. The rest (Interaction, Cutscene, Custom) are descriptive only for now
/// and need a <see cref="Handler"/> or a listener on <see cref="FiredEventHandler"/>. Those two
/// escape hatches stay open for every type -- an assigned Handler is called on top of the built-in
/// behaviour, not instead of it, so a volume can open a conversation *and* tell a quest log.
/// </para>
/// <para>
/// Two layers of filtering, and both matter. The Area3D's own collision mask decides what the
/// physics server even reports overlaps for -- set it to the Agent layer for a volume the player
/// walks into. <see cref="Initiators"/> then narrows that further, which is what stops a patrolling
/// NPC from setting off the player's cutscene.
/// </para>
/// <para>
/// Nothing is drawn at runtime. In the editor the volume is filled in permanently, selected or not,
/// by CollisionShapeGizmoPlugin -- see <see cref="ShowInEditor"/> -- in each CollisionShape3D's own
/// Debug Color, so a scene full of triggers can be read at a glance.
/// </para>
/// <para>
/// [Tool] so that the Inspector can hide the fields that don't apply to the chosen Type, warn about
/// a volume that can never fire, and keep the editor fill in sync. No game logic runs in the editor.
/// </para>
/// </summary>
[Tool]
public partial class AreaTrigger : Area3D
{
    private TriggerType _type = TriggerType.Dialogue;
    private string _chatDialogId = "";
    private string _scenePath = "";
    private TriggerHandler _handler;
    private bool _showInEditor = true;

    /// <summary>
    /// What this trigger is for, and for Dialogue/SceneLoad what it does. Carried on every event,
    /// so a handler shared by several volumes can branch on it.
    /// </summary>
    [Export]
    public TriggerType Type
    {
        get => _type;
        set
        {
            _type = value;
            OnAuthoredChange();
        }
    }

    /// <summary>
    /// The Dialog row to open, when Type is Dialogue. Empty opens nothing.
    /// <para>
    /// This gets the Inspector's Dialog picker -- a dropdown of the ids that exist, red when what's
    /// typed isn't one of them -- for free: DialogInspectorPlugin matches on the property name, not
    /// on the node type, so it is the same field AgentAI gets.
    /// </para>
    /// </summary>
    [Export]
    public string ChatDialogId
    {
        get => _chatDialogId;
        set
        {
            _chatDialogId = value;
            OnAuthoredChange();
        }
    }

    /// <summary>Skips the event if a dialog is already on screen, rather than cutting it off mid-line.</summary>
    [Export]
    public bool SkipWhileDialogOpen { get; set; } = true;

    /// <summary>
    /// Halts whoever walked in, so the box opens over a character standing still rather than one
    /// striding on across the scene while the narrator talks. Uncheck for a volume that should
    /// narrate over movement -- a line that plays as you cross a bridge, say.
    /// </summary>
    [Export]
    public bool StopInitiator { get; set; } = true;

    /// <summary>The .tscn to load, when Type is Scene Load. Empty loads nothing.</summary>
    [Export(PropertyHint.File, "*.tscn")]
    public string ScenePath
    {
        get => _scenePath;
        set
        {
            _scenePath = value;
            OnAuthoredChange();
        }
    }

    /// <summary>
    /// A handler node to call as well as the built-in behaviour -- the way to react to the types
    /// that have none yet, and the way to add a second reaction to the ones that do. Optional.
    /// </summary>
    [Export]
    public TriggerHandler Handler
    {
        get => _handler;
        set
        {
            _handler = value;
            OnAuthoredChange();
        }
    }

    [ExportGroup("Firing")]
    /// <summary>Which crossings fire the trigger. Both may be checked; the event says which one happened.</summary>
    [Export(PropertyHint.Flags, "Entered:1,Exited:2")]
    public TriggerPhase FireOn { get; set; } = TriggerPhase.Entered;

    /// <summary>Which bodies count, on top of the Area3D's collision mask.</summary>
    [Export]
    public TriggerInitiators Initiators { get; set; } = TriggerInitiators.Avatar;

    /// <summary>
    /// Fires once for the whole scene and then goes quiet -- the usual thing for an intro
    /// conversation or a cutscene. Uncheck for volumes that should keep reacting (a door, a music
    /// zone); <see cref="ReArmSeconds"/> then governs how soon.
    /// </summary>
    [Export]
    public bool OneShot { get; set; } = true;

    /// <summary>
    /// Minimum seconds between fires when not <see cref="OneShot"/>. Stops a body loitering on the
    /// boundary from re-firing every frame. 0 means no cooldown.
    /// </summary>
    [Export(PropertyHint.Range, "0,60,0.1,or_greater")]
    public float ReArmSeconds { get; set; } = 0.5f;

    /// <summary>
    /// Authored string handed to the handler -- which door, which cutscene, which spawn point.
    /// Only needed when one handler serves several triggers.
    /// </summary>
    [Export]
    public string Payload { get; set; } = "";

    [ExportGroup("Editor")]
    /// <summary>
    /// Fills this volume in the editor viewport whether or not it is selected. Uncheck to quiet a
    /// volume that is in the way of the scene behind it; it then shows only while selected, like
    /// any other collider. Editor-only either way -- nothing is drawn in game.
    /// </summary>
    [Export]
    public bool ShowInEditor
    {
        get => _showInEditor;
        set
        {
            _showInEditor = value;
            RefreshShapeGizmos();
        }
    }

    /// <summary>Raised whenever this trigger fires, handler or not, for listeners that aren't the handler.</summary>
    [Signal]
    public delegate void FiredEventHandler(TriggerEvent triggerEvent);

    /// <summary>How many times this trigger has fired since the scene loaded.</summary>
    public int FireCount { get; private set; }

    private TriggerHandler _builtInHandler;
    private double _lastFireTime = double.NegativeInfinity;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            RefreshShapeGizmos();
            return;
        }

        _builtInHandler = CreateBuiltInHandler();
        if (_builtInHandler != null)
        {
            AddChild(_builtInHandler);
        }

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    /// <summary>
    /// The Type dropdown's behaviour, as a real handler node rather than an inline branch in
    /// <see cref="TryFire"/>: it is the same TriggerHandler_Dialogue/TriggerHandler_LoadScene an
    /// author could have wired up by hand, so there is one implementation of "open the box safely"
    /// and one of "swap the scene deferred", and its Handled signal goes out either way.
    /// Null for the types with no built-in behaviour, and for a half-authored volume (Type is
    /// Dialogue but no id picked yet) -- <see cref="_GetConfigurationWarnings"/> flags that case.
    /// </summary>
    private TriggerHandler CreateBuiltInHandler() => Type switch
    {
        TriggerType.Dialogue when !string.IsNullOrEmpty(ChatDialogId) => new TriggerHandler_Dialogue
        {
            Name = "DialogueHandler",
            ChatDialogId = ChatDialogId,
            SkipWhileDialogOpen = SkipWhileDialogOpen,
            StopInitiator = StopInitiator,
        },
        TriggerType.SceneLoad when !string.IsNullOrEmpty(ScenePath) => new TriggerHandler_LoadScene
        {
            Name = "LoadSceneHandler",
            ScenePath = ScenePath,
        },
        _ => null,
    };

    private void OnBodyEntered(Node3D body) => TryFire(body, TriggerPhase.Entered);

    private void OnBodyExited(Node3D body) => TryFire(body, TriggerPhase.Exited);

    private void TryFire(Node3D body, TriggerPhase phase)
    {
        if ((FireOn & phase) == 0 || body == null)
        {
            return;
        }

        if (OneShot && FireCount > 0)
        {
            return;
        }

        double now = Time.GetTicksMsec() / 1000.0;
        if (!OneShot && ReArmSeconds > 0f && now - _lastFireTime < ReArmSeconds)
        {
            return;
        }

        // Same lookup AITask_TriggerDialog uses to reach the other party's AI: the AgentAI lives on
        // a child node of the body, not on the body itself.
        var agent = body.GetNodeOrNull<AgentAI>("AgentAI");
        if (!IsAllowedInitiator(agent))
        {
            return;
        }

        FireCount++;
        _lastFireTime = now;

        var triggerEvent = new TriggerEvent
        {
            Type = Type,
            Phase = phase,
            Source = this,
            Initiator = body,
            InitiatorAgent = agent,
            Position = body.GlobalPosition,
            Timestamp = now,
            FireCount = FireCount,
            Payload = Payload,
        };

        _builtInHandler?.Handle(triggerEvent);
        Handler?.Handle(triggerEvent);
        EmitSignal(SignalName.Fired, triggerEvent);
    }

    private bool IsAllowedInitiator(AgentAI agent) => Initiators switch
    {
        TriggerInitiators.Agents => agent != null,
        TriggerInitiators.Avatar => agent is AvatarAgentAI,
        _ => true,
    };

    /// <summary>Hides the per-type fields that don't apply to the chosen <see cref="Type"/>.</summary>
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        var name = property["name"].AsStringName();
        if (AppliesToCurrentType(name))
        {
            return;
        }

        // Clearing Editor rather than blanking usage outright: Storage stays on, so an id typed
        // against one Type survives a look at another and comes back when the author switches back.
        var usage = (PropertyUsageFlags)(int)property["usage"];
        property["usage"] = (int)(usage & ~PropertyUsageFlags.Editor);
    }

    private bool AppliesToCurrentType(StringName name)
    {
        if (name == PropertyName.ChatDialogId
            || name == PropertyName.SkipWhileDialogOpen
            || name == PropertyName.StopInitiator)
        {
            return Type == TriggerType.Dialogue;
        }

        if (name == PropertyName.ScenePath)
        {
            return Type == TriggerType.SceneLoad;
        }

        return true;
    }

    // Godot never re-reads a node's configuration warnings on its own, and the Type dropdown decides
    // which of the per-type fields are even shown, so both have to be poked by hand whenever an
    // authored value changes.
    private void OnAuthoredChange()
    {
        if (!Engine.IsEditorHint())
        {
            return;
        }

        NotifyPropertyListChanged();
        UpdateConfigurationWarnings();
    }

    /// <summary>
    /// The permanent fill is drawn by the CollisionShape3D's own gizmo, which reads
    /// <see cref="ShowInEditor"/> off this parent -- so a change here has to ask the children to
    /// redraw, since nothing about *them* changed.
    /// </summary>
    private void RefreshShapeGizmos()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
        {
            return;
        }

        foreach (var child in GetChildren())
        {
            (child as Node3D)?.UpdateGizmos();
        }
    }

    public override string[] _GetConfigurationWarnings()
    {
        var warnings = new List<string>();

        if (!HasCollisionShape())
        {
            warnings.Add("No CollisionShape3D child -- this trigger has no volume and can never fire.");
        }

        if (CollisionMask == 0)
        {
            warnings.Add("Collision Mask is empty -- no body will ever be reported to this trigger. "
                         + "Enable the layer the initiators are on (Agent, for the avatar).");
        }

        if (FireOn == TriggerPhase.None)
        {
            warnings.Add("Fire On has neither Entered nor Exited checked -- this trigger can never fire.");
        }

        if (!HasBuiltInBehaviour && Handler == null && !HasConnections(SignalName.Fired))
        {
            warnings.Add(Type switch
            {
                TriggerType.Dialogue =>
                    "Type is Dialogue but Chat Dialog Id is empty -- this trigger opens nothing. "
                    + "Pick a Dialog, or assign a Handler / connect Fired.",
                TriggerType.SceneLoad =>
                    "Type is Scene Load but Scene Path is empty -- this trigger loads nothing. "
                    + "Pick a scene, or assign a Handler / connect Fired.",
                _ =>
                    $"Type is {Type}, which has no built-in behaviour yet -- assign a Handler or "
                    + "connect Fired, or this trigger does nothing.",
            });
        }

        return warnings.ToArray();
    }

    private bool HasBuiltInBehaviour => Type switch
    {
        TriggerType.Dialogue => !string.IsNullOrEmpty(ChatDialogId),
        TriggerType.SceneLoad => !string.IsNullOrEmpty(ScenePath),
        _ => false,
    };

    private bool HasCollisionShape()
    {
        foreach (var child in GetChildren())
        {
            if (child is CollisionShape3D or CollisionPolygon3D)
            {
                return true;
            }
        }

        return false;
    }
}
