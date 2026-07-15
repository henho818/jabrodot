namespace Jabroni.AI;

public sealed class AIStateCondition_NoChatTarget : AICondition
{
    public AIStateCondition_NoChatTarget(AgentAI agent) : base(agent)
    {
    }

    public override bool Evaluate() => Agent.ChatTarget == null;
}
