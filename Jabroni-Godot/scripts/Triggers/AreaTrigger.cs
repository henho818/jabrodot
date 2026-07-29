using System.Collections.Generic;
using Godot;
using Jabroni.AI;

namespace Jabroni.Triggers;

/// <summary>
/// An invisible volume in the world that fires an event when something walks into (or out of) it:
/// attach this to an Area3D with a CollisionShape3D child, pick what the trigger is for from the
/// Type dropdown, and drag the <see cref="TriggerHandler"/> that should react into the Handler slot.
/// <para>
/// The trigger itself only decides *whether* an event happened -- who is allowed to set it off,
/// which crossings count, whether it may fire again. What happens next is entirely the handler's;
/// nothing here knows about dialogue, cutscenes or scene loading. Leaving Handler empty is valid:
/// the <see cref="FiredEventHandler"/> signal still goes out, so several listeners can share one
/// volume, or a GDScript node can pick it up.
/// </para>
/// <para>
/// Two layers of filtering, and both matter. The Area3D's own collision mask decides what the
/// physics server even reports overlaps for -- set it to the Agent layer for a volume the player
/// walks into. <see cref="Initiators"/> then narrows that further, which is what stops a patrolling
/// NPC from setting off the player's cutscene. Nothing is drawn at runtime; in the editor the shape
/// is visible through CollisionShapeGizmoPlugin when it or the trigger is selected.
/// </para>
/// <para>
/// [Tool] purely so the Inspector can warn about a volume that can never fire (no shape, no mask,
/// no handler and nothing connected). No game logic runs in the editor.
/// </para>
/// </summary>
[Tool]
public partial class AreaTrigger : Area3D
{
    /// <summary>What this trigger is for. Passed through on the event; see <see cref="TriggerType"/>.</summary>
    [Export]
    public TriggerType Type { get; set; } = TriggerType.Dialogue;

    /// <summary>The node that reacts. Optional -- see the Fired signal.</summary>
    [Export]
    public TriggerHandler Handler { get; set; }

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

    /// <summary>Raised whenever this trigger fires, handler or not, for listeners that aren't the handler.</summary>
    [Signal]
    public delegate void FiredEventHandler(TriggerEvent triggerEvent);

    /// <summary>How many times this trigger has fired since the scene loaded.</summary>
    public int FireCount { get; private set; }

    private double _lastFireTime = double.NegativeInfinity;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            return;
        }

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

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

        Handler?.Handle(triggerEvent);
        EmitSignal(SignalName.Fired, triggerEvent);
    }

    private bool IsAllowedInitiator(AgentAI agent) => Initiators switch
    {
        TriggerInitiators.Agents => agent != null,
        TriggerInitiators.Avatar => agent is AvatarAgentAI,
        _ => true,
    };

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

        if (Handler == null && !HasConnections(SignalName.Fired))
        {
            warnings.Add("No Handler assigned and nothing connected to Fired -- this trigger does nothing.");
        }

        return warnings.ToArray();
    }

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
