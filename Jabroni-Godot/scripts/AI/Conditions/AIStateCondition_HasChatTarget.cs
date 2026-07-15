namespace Jabroni.AI;

public sealed class AIStateCondition_HasChatTarget : AICondition
{
    public AIStateCondition_HasChatTarget(AgentAI agent) : base(agent)
    {
    }

    public override bool Evaluate() => Agent.ChatTarget != null;
}
