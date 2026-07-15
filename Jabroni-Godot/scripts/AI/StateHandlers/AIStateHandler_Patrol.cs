using System.Collections.Generic;

namespace Jabroni.AI;

public sealed class AIStateHandler_Patrol : AIStateHandler
{
    public AIStateHandler_Patrol(AgentAI agent) : base(agent)
    {
    }

    protected override void PopulateTasks(List<AITask> tasks)
    {
        tasks.Add(new AITask_NavigatePath(Agent, Agent.PatrolPath));
    }
}
