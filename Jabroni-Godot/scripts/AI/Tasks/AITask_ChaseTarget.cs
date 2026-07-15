namespace Jabroni.AI;

/// <summary>Continuously chases Agent.AttackTarget, re-issuing MoveTo each frame so it tracks
/// a moving target; never completes on its own (the owning FSM's transitions end it).</summary>
public sealed class AITask_ChaseTarget : AITask
{
    public AITask_ChaseTarget(AgentAI agent) : base(agent)
    {
    }

    public override void Update(double delta)
    {
        var target = Agent.AttackTarget;
        if (target == null || Agent.Locomotion == null)
        {
            return;
        }

        Agent.Locomotion.MoveTo(target.GlobalPosition);
    }
}
