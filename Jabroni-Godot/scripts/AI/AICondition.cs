namespace Jabroni.AI;

public abstract class AICondition
{
    protected AgentAI Agent { get; }

    protected AICondition(AgentAI agent)
    {
        Agent = agent;
    }

    public abstract bool Evaluate();
}
