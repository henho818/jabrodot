using System.Collections.Generic;

namespace Jabroni.AI;

public sealed class AIStateHandler_NavToAgent : AIStateHandler
{
    public AIStateHandler_NavToAgent(AgentAI agent) : base(agent)
    {
    }

    protected override void PopulateTasks(List<AITask> tasks)
    {
        tasks.Add(new AITask_NavToAgent(Agent));
    }

    public override void ExitState()
    {
        // Only reached via arrival (see AIStateMachine_Avatar) since the cancel path
        // clears ChatTarget before this fires -- so a non-null target here means "we
        // actually arrived," and it's time to let the other party know to start chatting.
        var target = Agent.ChatTarget;
        var targetAi = target?.GetNodeOrNull<AgentAI>("AgentAI");
        if (targetAi != null)
        {
            targetAi.ChatTarget = Agent.Body;
        }
    }
}
