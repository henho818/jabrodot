using Godot;
using Jabroni.AI;

namespace Jabroni.Triggers;

/// <summary>
/// One crossing of one <see cref="AreaTrigger"/>: everything a <see cref="TriggerHandler"/> could
/// want to know about why it was just called. Built by the trigger, passed to the handler, and
/// emitted alongside the Fired/Handled signals.
/// <para>
/// A <see cref="RefCounted"/> rather than a plain C# class so it can travel through those signals
/// (Godot only marshals GodotObjects) and so GDScript can read it too. Constructed with an object
/// initializer -- the properties are init-only, since an event describes something that has
/// already happened and nothing downstream should be rewriting it.
/// </para>
/// </summary>
public partial class TriggerEvent : RefCounted
{
    /// <summary>The trigger's authored <see cref="TriggerType"/> -- what this trigger is *for*.</summary>
    public TriggerType Type { get; init; }

    /// <summary>Exactly one of Entered/Exited: the crossing that just happened.</summary>
    public TriggerPhase Phase { get; init; }

    /// <summary>The trigger that raised this -- the script that fired the event.</summary>
    public AreaTrigger Source { get; init; }

    /// <summary>The body that crossed the volume (the avatar's CharacterBody3D, an NPC, ...).</summary>
    public Node3D Initiator { get; init; }

    /// <summary>The <see cref="Initiator"/>'s AgentAI, if it has one -- null for scenery or props.</summary>
    public AgentAI InitiatorAgent { get; init; }

    /// <summary>Where the initiator was when it crossed -- e.g. which side of a door it came from.</summary>
    public Vector3 Position { get; init; }

    /// <summary>
    /// Seconds since engine start (Time.GetTicksMsec), the same clock AgentAI stamps its
    /// disturbance/target times with, so the two can be compared directly.
    /// </summary>
    public double Timestamp { get; init; }

    /// <summary>How many times the source trigger has fired, including this one (1 on the first).</summary>
    public int FireCount { get; init; }

    /// <summary>
    /// Free-form authored string from the trigger, for handlers shared by several triggers: which
    /// door, which cutscene, which spawn point. Empty when unused.
    /// </summary>
    public string Payload { get; init; } = "";

    /// <summary>True when the player-controlled avatar is what crossed the volume.</summary>
    public bool FromAvatar => InitiatorAgent is AvatarAgentAI;

    public override string ToString() =>
        $"{Type}/{Phase} from '{Initiator?.Name}' via '{Source?.Name}' "
        + $"#{FireCount} @{Timestamp:F2}s"
        + (string.IsNullOrEmpty(Payload) ? "" : $" payload='{Payload}'");
}
