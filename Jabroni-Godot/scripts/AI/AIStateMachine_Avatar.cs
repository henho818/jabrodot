using System.Collections.Generic;

namespace Jabroni.AI;

public sealed class AIStateMachine_Avatar : AIStateMachine
{
    public AIStateMachine_Avatar(AgentAI agent) : base(agent)
    {
    }

    protected override AIState GetInitialState() => AIState.Idle;

    protected override void BuildTransitions(List<AIStateTransition> transitions)
    {
        transitions.Add(new AIStateTransition(AIState.Idle, AIState.NavToAgent,
            new AIStateCondition_HasChatTarget(Agent)));

        transitions.Add(new AIStateTransition(AIState.NavToAgent, AIState.Chatting,
            new AIStateCondition_HasArrived(Agent)));

        // Covers clicking the ground mid-approach (ClickToMove.MoveToGround clears
        // ChatTarget before this evaluates), so a canceled approach falls back to Idle
        // instead of getting stuck in NavToAgent.
        transitions.Add(new AIStateTransition(AIState.NavToAgent, AIState.Idle,
            new AIStateCondition_NoChatTarget(Agent)));

        transitions.Add(new AIStateTransition(AIState.Chatting, AIState.Idle,
            new AIStateCondition_NoChatTarget(Agent)));
    }

    protected override AIStateHandler CreateHandler(AIState state)
    {
        return state switch
        {
            AIState.Idle => new AIStateHandler_Idle(Agent),
            AIState.NavToAgent => new AIStateHandler_NavToAgent(Agent),
            AIState.Chatting => new AIStateHandler_FaceAndWait(Agent),
            _ => null,
        };
    }
}
