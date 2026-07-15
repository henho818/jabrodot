using System.Collections.Generic;

namespace Jabroni.AI;

/// <summary>A (from, to) state pair gated by conditions that are OR'd together.</summary>
public sealed class AIStateTransition
{
    public AIState From { get; }
    public AIState To { get; }

    private readonly List<AICondition> _conditions;

    public AIStateTransition(AIState from, AIState to, params AICondition[] conditions)
    {
        From = from;
        To = to;
        _conditions = new List<AICondition>(conditions);
    }

    public bool Matches(AIState currentState) => From == currentState || From == AIState.Any;

    public bool ConditionsMet()
    {
        if (_conditions.Count == 0)
        {
            return true;
        }

        foreach (var condition in _conditions)
        {
            if (condition.Evaluate())
            {
                return true;
            }
        }

        return false;
    }
}
