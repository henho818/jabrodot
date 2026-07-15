using System.Collections.Generic;

namespace Jabroni.AI;

/// <summary>No tasks: the agent simply stands still until a transition fires.</summary>
public sealed class AIStateHandler_Idle : AIStateHandler
{
    public AIStateHandler_Idle(AgentAI agent) : base(agent)
    {
    }

    protected override void PopulateTasks(List<AITask> tasks)
    {
    }
}
