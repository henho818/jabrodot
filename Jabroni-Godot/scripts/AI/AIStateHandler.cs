using System.Collections.Generic;

namespace Jabroni.AI;

/// <summary>Owns an ordered task list for one state; runs tasks sequentially.</summary>
public abstract class AIStateHandler
{
    protected AgentAI Agent { get; }

    private readonly List<AITask> _tasks = new();
    private int _taskIndex;

    protected AIStateHandler(AgentAI agent)
    {
        Agent = agent;
    }

    public void EnterState()
    {
        _tasks.Clear();
        PopulateTasks(_tasks);
        _taskIndex = 0;
        StartCurrentTask();
    }

    public void UpdateState(double delta)
    {
        if (_taskIndex >= _tasks.Count)
        {
            return;
        }

        var task = _tasks[_taskIndex];
        task.Update(delta);

        if (task.IsComplete)
        {
            task.End();
            _taskIndex++;
            StartCurrentTask();
        }
    }

    public virtual void ExitState()
    {
    }

    protected abstract void PopulateTasks(List<AITask> tasks);

    private void StartCurrentTask()
    {
        if (_taskIndex < _tasks.Count)
        {
            _tasks[_taskIndex].Start();
        }
    }
}
