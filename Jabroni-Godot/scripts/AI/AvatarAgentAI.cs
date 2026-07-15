using Godot;

namespace Jabroni.AI;

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
