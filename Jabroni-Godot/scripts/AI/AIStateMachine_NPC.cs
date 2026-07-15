using System.Collections.Generic;

namespace Jabroni.AI;

public sealed class AIStateMachine_NPC : AIStateMachine
{
    public AIStateMachine_NPC(AgentAI agent) : base(agent)
    {
    }

    protected override AIState GetInitialState() => AIState.Idle;

    protected override void BuildTransitions(List<AIStateTransition> transitions)
    {
        // Highest priority: a chat target always pulls the agent into Chatting, from any state.
        transitions.Add(new AIStateTransition(AIState.Any, AIState.Chatting,
            new AIStateCondition_HasChatTarget(Agent)));

        transitions.Add(new AIStateTransition(AIState.Chatting, AIState.Alert,
            new AIStateCondition_NoChatTarget(Agent)));

        transitions.Add(new AIStateTransition(AIState.Idle, AIState.Patrol,
            new AIStateCondition_HasPatrolPath(Agent)));

        transitions.Add(new AIStateTransition(AIState.Patrol, AIState.Alert,
            new AIStateCondition_EnemyDisturbance(Agent)));

        transitions.Add(new AIStateTransition(AIState.Alert, AIState.Patrol,
            new AIStateCondition_TimeSinceLastDisturbance(Agent, Agent.Stats.AlertDisengageTime)));
    }

    protected override AIStateHandler CreateHandler(AIState state)
    {
        return state switch
        {
            AIState.Idle => new AIStateHandler_Idle(Agent),
            AIState.Patrol => new AIStateHandler_Patrol(Agent),
            AIState.Alert => new AIStateHandler_Alert(Agent),
            AIState.Chatting => new AIStateHandler_Chatting(Agent),
            _ => null,
        };
    }
}
