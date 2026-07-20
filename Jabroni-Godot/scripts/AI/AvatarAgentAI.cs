using Godot;

namespace Jabroni.AI;

/// <summary>[Tool] so the editor Inspector calls into AgentAI's _GetPropertyList/_Get -- Godot's C# tool-attribute check is per concrete script type, not inherited from AgentAI (verified against GodotSharp 4.7's ScriptManagerBridge.GetScriptTypeInfo).</summary>
[Tool]
public partial class AvatarAgentAI : AgentAI
{
    protected override string ConfigId => "AC.Avatar";

    /// <summary>Moves directly to a ground point, canceling any chat approach in progress.</summary>
    public void MoveToGround(Vector3 destination)
    {
        ChatTarget = null;
        Locomotion?.MoveTo(destination);
    }

    protected override AIStateMachine CreateStateMachine() => new AIStateMachine_Avatar(this);
}
