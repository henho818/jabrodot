using System.Collections.Generic;

namespace Jabroni.AI;

/// <summary>Full predator loop: Patrol -> Alert -> Search -> Attack -> Search -> Alert ->
/// Patrol, with Any -> Chatting taking priority throughout (mirrors the source project's
/// AIStateMachine_Shark graph, including the otherwise-odd chat transition it kept).</summary>
public sealed class AIStateMachine_Shark : AIStateMachine
{
    public AIStateMachine_Shark(AgentAI agent) : base(agent)
    {
    }

    protected override AIState GetInitialState() => AIState.Idle;

    protected override void BuildTransitions(List<AIStateTransition> transitions)
    {
        transitions.Add(new AIStateTransition(AIState.Any, AIState.Chatting,
            new AIStateCondition_HasChatTarget(Agent)));

        transitions.Add(new AIStateTransition(AIState.Chatting, AIState.Alert,
            new AIStateCondition_NoChatTarget(Agent)));

        transitions.Add(new AIStateTransition(AIState.Idle, AIState.Patrol,
            new AIStateCondition_HasPatrolPath(Agent)));

        transitions.Add(new AIStateTransition(AIState.Patrol, AIState.Alert,
            new AIStateCondition_EnemyDisturbance(Agent)));

        // Target acquisition takes priority over disengaging when both are momentarily true.
        transitions.Add(new AIStateTransition(AIState.Alert, AIState.Search,
            new AIStateCondition_HasTarget(Agent)));

        transitions.Add(new AIStateTransition(AIState.Alert, AIState.Patrol,
            new AIStateCondition_TimeSinceLastDisturbance(Agent, Agent.Stats.AlertDisengageTime)));

        transitions.Add(new AIStateTransition(AIState.Search, AIState.Attack,
            new AIStateCondition_DistanceToTarget(Agent, Agent.Stats.AttackDistance, lessThan: true)));

        transitions.Add(new AIStateTransition(AIState.Search, AIState.Alert,
            new AIStateCondition_TimeSinceLastTargetAcquired(Agent, Agent.Stats.SearchDisengageTime)));

        transitions.Add(new AIStateTransition(AIState.Attack, AIState.Search,
            new AIStateCondition_DistanceToTarget(Agent, Agent.Stats.AttackDistance, lessThan: false)));
    }

    protected override AIStateHandler CreateHandler(AIState state)
    {
        return state switch
        {
            AIState.Idle => new AIStateHandler_Idle(Agent),
            AIState.Patrol => new AIStateHandler_Patrol(Agent),
            AIState.Alert => new AIStateHandler_Alert(Agent),
            AIState.Search => new AIStateHandler_Search(Agent),
            AIState.Attack => new AIStateHandler_Attack(Agent),
            AIState.Chatting => new AIStateHandler_FaceAndWait(Agent),
            _ => null,
        };
    }
}
