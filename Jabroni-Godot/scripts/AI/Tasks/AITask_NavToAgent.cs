using Godot;

namespace Jabroni.AI;

/// <summary>Walks to social distance from Agent.ChatTarget; completes once the agent arrives
/// (checked via AIStateCondition_HasArrived, not by this task itself).</summary>
public sealed class AITask_NavToAgent : AITask
{
    private const float SocialDistance = 2.5f;

    public AITask_NavToAgent(AgentAI agent) : base(agent)
    {
    }

    public override void Start()
    {
        var target = Agent.ChatTarget;
        if (target == null || Agent.Locomotion == null)
        {
            IsComplete = true;
            return;
        }

        Vector3 targetPos = target.GlobalPosition;
        Vector3 away = Agent.Body.GlobalPosition - targetPos;
        away.Y = 0f;
        if (away.LengthSquared() < 0.0001f)
        {
            away = Vector3.Forward;
        }

        Vector3 stopPoint = targetPos + away.Normalized() * SocialDistance;
        Agent.Locomotion.MoveTo(stopPoint);
    }

    public override void Update(double delta)
    {
        if (Agent.Locomotion != null && Agent.Locomotion.HasArrived)
        {
            IsComplete = true;
        }
    }
}
