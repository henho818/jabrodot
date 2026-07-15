using Godot;

namespace Jabroni.AI;

/// <summary>True once the target hasn't been sighted for thresholdSeconds; also clears
/// Agent.AttackTarget at that moment (stale target shouldn't linger once "lost").</summary>
public sealed class AIStateCondition_TimeSinceLastTargetAcquired : AICondition
{
    private readonly double _thresholdSeconds;

    public AIStateCondition_TimeSinceLastTargetAcquired(AgentAI agent, double thresholdSeconds) : base(agent)
    {
        _thresholdSeconds = thresholdSeconds;
    }

    public override bool Evaluate()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - Agent.LastTargetAcquiredTime < _thresholdSeconds)
        {
            return false;
        }

        Agent.ClearAttackTarget();
        return true;
    }
}
