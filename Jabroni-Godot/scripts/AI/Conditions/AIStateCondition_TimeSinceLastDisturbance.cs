using Godot;

namespace Jabroni.AI;

/// <summary>True once at least thresholdSeconds have passed since the last reported disturbance.</summary>
public sealed class AIStateCondition_TimeSinceLastDisturbance : AICondition
{
    private readonly double _thresholdSeconds;

    public AIStateCondition_TimeSinceLastDisturbance(AgentAI agent, double thresholdSeconds) : base(agent)
    {
        _thresholdSeconds = thresholdSeconds;
    }

    public override bool Evaluate()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        return now - Agent.LastDisturbanceTime >= _thresholdSeconds;
    }
}
