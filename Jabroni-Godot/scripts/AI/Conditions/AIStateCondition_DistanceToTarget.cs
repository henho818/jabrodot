namespace Jabroni.AI;

public sealed class AIStateCondition_DistanceToTarget : AICondition
{
    private readonly float _threshold;
    private readonly bool _lessThan;

    public AIStateCondition_DistanceToTarget(AgentAI agent, float threshold, bool lessThan) : base(agent)
    {
        _threshold = threshold;
        _lessThan = lessThan;
    }

    public override bool Evaluate()
    {
        if (Agent.AttackTarget == null)
        {
            return false;
        }

        float distance = Agent.Body.GlobalPosition.DistanceTo(Agent.AttackTarget.GlobalPosition);
        return _lessThan ? distance < _threshold : distance > _threshold;
    }
}
