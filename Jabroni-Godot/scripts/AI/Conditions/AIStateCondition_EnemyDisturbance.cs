using Godot;

namespace Jabroni.AI;

/// <summary>True while a disturbance was reported very recently (i.e. something is nearby right now).</summary>
public sealed class AIStateCondition_EnemyDisturbance : AICondition
{
    private const double RecentThresholdSeconds = 0.2;

    public AIStateCondition_EnemyDisturbance(AgentAI agent) : base(agent)
    {
    }

    public override bool Evaluate()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        return now - Agent.LastDisturbanceTime < RecentThresholdSeconds;
    }
}
