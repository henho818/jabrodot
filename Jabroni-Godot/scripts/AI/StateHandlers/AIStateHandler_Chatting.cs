using System.Collections.Generic;

namespace Jabroni.AI;

public sealed class AIStateHandler_Chatting : AIStateHandler
{
    public AIStateHandler_Chatting(AgentAI agent) : base(agent)
    {
    }

    protected override void PopulateTasks(List<AITask> tasks)
    {
        tasks.Add(new AITask_FacePosition(Agent, () => Agent.ChatTarget?.GlobalPosition, autoCompleteAfterSeconds: 0.4));
        tasks.Add(new AITask_TriggerDialog(Agent));
    }
}
