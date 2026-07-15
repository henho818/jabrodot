using System.Collections.Generic;

namespace Jabroni.AI;

public sealed class AIStateHandler_Attack : AIStateHandler
{
    public AIStateHandler_Attack(AgentAI agent) : base(agent)
    {
    }

    protected override void PopulateTasks(List<AITask> tasks)
    {
        tasks.Add(new AITask_AttackTarget(Agent));
    }
}
