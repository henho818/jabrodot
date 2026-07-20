using Godot;

namespace Jabroni.AI;

/// <summary>[Tool] so the editor Inspector calls into AgentAI's _GetPropertyList/_Get -- Godot's C# tool-attribute check is per concrete script type, not inherited from AgentAI (verified against GodotSharp 4.7's ScriptManagerBridge.GetScriptTypeInfo).</summary>
[Tool]
public partial class SharkAgentAI : AgentAI
{
    protected override string ConfigId => "AC.Shark";
    protected override bool SnapPatrolPathToTerrain => false; // swims at a fixed depth, not on the terrain surface

    protected override AIStateMachine CreateStateMachine() => new AIStateMachine_Shark(this);
}
