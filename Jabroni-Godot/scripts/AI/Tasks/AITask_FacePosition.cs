using System;
using Godot;

namespace Jabroni.AI;

/// <summary>
/// Turns the agent to face a (possibly moving) world position. If autoCompleteAfterSeconds
/// is set, the task completes after that duration; otherwise it runs until the owning
/// state exits (used for "keep facing while alert/chatting" behavior).
/// </summary>
public sealed class AITask_FacePosition : AITask
{
    private readonly Func<Vector3?> _targetProvider;
    private readonly double? _autoCompleteAfterSeconds;
    private double _elapsed;

    public AITask_FacePosition(AgentAI agent, Func<Vector3?> targetProvider, double? autoCompleteAfterSeconds = null)
        : base(agent)
    {
        _targetProvider = targetProvider;
        _autoCompleteAfterSeconds = autoCompleteAfterSeconds;
    }

    public override void Start()
    {
        _elapsed = 0;
        Agent.Locomotion?.Stop();
    }

    public override void Update(double delta)
    {
        var target = _targetProvider();
        if (target.HasValue)
        {
            Agent.Locomotion?.FaceWorldPosition(target.Value);
        }

        if (!_autoCompleteAfterSeconds.HasValue)
        {
            return;
        }

        _elapsed += delta;
        if (_elapsed >= _autoCompleteAfterSeconds.Value)
        {
            IsComplete = true;
        }
    }
}
