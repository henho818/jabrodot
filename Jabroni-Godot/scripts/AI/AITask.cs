namespace Jabroni.AI;

/// <summary>
/// One step within a state's task sequence. Polled (Start/Update/End), not async, so a
/// task can be cleanly abandoned mid-flight if the owning state exits early -- see
/// AIStateHandler.
/// </summary>
public abstract class AITask
{
    protected AgentAI Agent { get; }

    protected AITask(AgentAI agent)
    {
        Agent = agent;
    }

    public bool IsComplete { get; protected set; }

    public virtual void Start()
    {
    }

    public virtual void Update(double delta)
    {
    }

    public virtual void End()
    {
    }
}
