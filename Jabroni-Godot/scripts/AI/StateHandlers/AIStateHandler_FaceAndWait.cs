using System.Collections.Generic;

namespace Jabroni.AI;

/// <summary>Holds facing toward the chat target indefinitely; the owning FSM's transitions
/// (not this handler) decide when to leave, based on Agent.ChatTarget clearing.</summary>
public sealed class AIStateHandler_FaceAndWait : AIStateHandler
{
    public AIStateHandler_FaceAndWait(AgentAI agent) : base(agent)
    {
    }

    protected override void PopulateTasks(List<AITask> tasks)
    {
        tasks.Add(new AITask_FacePosition(Agent, () => Agent.ChatTarget?.GlobalPosition));
    }
}
