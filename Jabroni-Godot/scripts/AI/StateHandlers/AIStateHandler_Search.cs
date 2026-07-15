using System.Collections.Generic;

namespace Jabroni.AI;

public sealed class AIStateHandler_Search : AIStateHandler
{
    public AIStateHandler_Search(AgentAI agent) : base(agent)
    {
    }

    protected override void PopulateTasks(List<AITask> tasks)
    {
        tasks.Add(new AITask_ChaseTarget(Agent));
    }
}
