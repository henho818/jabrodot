namespace Jabroni.AI;

public sealed class AIStateCondition_HasPatrolPath : AICondition
{
    public AIStateCondition_HasPatrolPath(AgentAI agent) : base(agent)
    {
    }

    public override bool Evaluate() => Agent.PatrolPathReady && Agent.PatrolPath != null && Agent.PatrolPath.Nodes.Count > 0;
}
