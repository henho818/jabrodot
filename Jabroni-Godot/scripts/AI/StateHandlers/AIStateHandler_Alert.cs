using System.Collections.Generic;

namespace Jabroni.AI;

public sealed class AIStateHandler_Alert : AIStateHandler
{
    public AIStateHandler_Alert(AgentAI agent) : base(agent)
    {
    }

    protected override void PopulateTasks(List<AITask> tasks)
    {
        tasks.Add(new AITask_FacePosition(Agent, () => Agent.DisturbancePosition));
    }
}
