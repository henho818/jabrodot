namespace Jabroni.AI;

public sealed class AIStateCondition_HasArrived : AICondition
{
    public AIStateCondition_HasArrived(AgentAI agent) : base(agent)
    {
    }

    public override bool Evaluate() => Agent.Locomotion != null && Agent.Locomotion.HasArrived;
}
