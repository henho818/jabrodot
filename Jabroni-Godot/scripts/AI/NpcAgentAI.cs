using Godot;

namespace Jabroni.AI;

/// <summary>[Tool] so the editor Inspector calls into AgentAI's _GetPropertyList/_Get -- Godot's C# tool-attribute check is per concrete script type, not inherited from AgentAI (verified against GodotSharp 4.7's ScriptManagerBridge.GetScriptTypeInfo).</summary>
[Tool]
public partial class NpcAgentAI : AgentAI
{
    protected override string ConfigId => "AC.Rocky";

    protected override AIStateMachine CreateStateMachine() => new AIStateMachine_NPC(this);
}
