using System.Collections.Generic;

namespace Jabroni.AI;

/// <summary>
/// Evaluates transitions in order each frame (first match wins), swaps state handlers on
/// change, then updates the current handler's tasks. Concrete archetypes (NPC, Avatar,
/// Shark) subclass this to build their own transition graph and handler set.
/// </summary>
public abstract class AIStateMachine
{
    protected AgentAI Agent { get; }

    private readonly List<AIStateTransition> _transitions = new();
    private readonly Dictionary<AIState, AIStateHandler> _handlers = new();

    public AIState CurrentState { get; private set; } = AIState.None;

    protected AIStateMachine(AgentAI agent)
    {
        Agent = agent;
    }

    public void Init()
    {
        BuildTransitions(_transitions);
        SetState(GetInitialState());
    }

    public void Update(double delta)
    {
        foreach (var transition in _transitions)
        {
            if (transition.Matches(CurrentState) && transition.ConditionsMet())
            {
                SetState(transition.To);
                break;
            }
        }

        CurrentHandler?.UpdateState(delta);
    }

    private AIStateHandler CurrentHandler =>
        _handlers.TryGetValue(CurrentState, out var handler) ? handler : null;

    private void SetState(AIState newState)
    {
        if (newState == CurrentState)
        {
            return;
        }

        CurrentHandler?.ExitState();
        CurrentState = newState;

        if (!_handlers.TryGetValue(newState, out var handler))
        {
            handler = CreateHandler(newState);
            if (handler != null)
            {
                _handlers[newState] = handler;
            }
        }

        handler?.EnterState();
    }

    protected abstract AIState GetInitialState();
    protected abstract void BuildTransitions(List<AIStateTransition> transitions);
    protected abstract AIStateHandler CreateHandler(AIState state);
}
