namespace Jabroni.Triggers;

/// <summary>
/// Which bodies are allowed to fire an <see cref="AreaTrigger"/>, on top of the physics mask the
/// Area3D already filters by. The mask decides what the area is told about at all; this decides
/// what counts once it has been told -- e.g. an area on the Agent layer that only the player,
/// not a wandering NPC, should set off.
/// </summary>
public enum TriggerInitiators
{
    /// <summary>Any body the Area3D's collision mask lets through.</summary>
    Any,

    /// <summary>Bodies carrying an AgentAI child (NPCs, the shark, the avatar).</summary>
    Agents,

    /// <summary>Only the player-controlled avatar.</summary>
    Avatar,
}
