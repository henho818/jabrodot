namespace Jabroni.AI;

public partial class NpcAgentAI : AgentAI
{
    protected override string ConfigId => "AC.Rocky";

    protected override AIStateMachine CreateStateMachine() => new AIStateMachine_NPC(this);
}
