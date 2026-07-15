namespace Jabroni.AI;

public sealed class AIStateCondition_HasTarget : AICondition
{
    public AIStateCondition_HasTarget(AgentAI agent) : base(agent)
    {
    }

    public override bool Evaluate() => Agent.AttackTarget != null;
}
