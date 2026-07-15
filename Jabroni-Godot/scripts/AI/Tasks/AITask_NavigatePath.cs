using Jabroni.Nav;

namespace Jabroni.AI;

/// <summary>Walks a NavPath node by node, pausing at each node's StayDuration, looping if the path loops.</summary>
public sealed class AITask_NavigatePath : AITask
{
    private readonly NavPath _path;
    private int _nodeIndex;
    private double _waitTimer;
    private bool _waiting;

    public AITask_NavigatePath(AgentAI agent, NavPath path) : base(agent)
    {
        _path = path;
    }

    public override void Start()
    {
        _nodeIndex = 0;
        _waiting = false;

        if (_path == null || _path.Nodes.Count == 0)
        {
            IsComplete = true;
            return;
        }

        MoveToCurrentNode();
    }

    public override void Update(double delta)
    {
        if (IsComplete)
        {
            return;
        }

        if (_waiting)
        {
            _waitTimer -= delta;
            if (_waitTimer <= 0)
            {
                AdvanceNode();
            }

            return;
        }

        if (Agent.Locomotion != null && Agent.Locomotion.HasArrived)
        {
            _waiting = true;
            _waitTimer = _path.Nodes[_nodeIndex].StayDuration;
        }
    }

    private void AdvanceNode()
    {
        _waiting = false;
        _nodeIndex++;

        if (_nodeIndex >= _path.Nodes.Count)
        {
            if (_path.Looping)
            {
                _nodeIndex = 0;
            }
            else
            {
                IsComplete = true;
                return;
            }
        }

        MoveToCurrentNode();
    }

    private void MoveToCurrentNode()
    {
        Agent.Locomotion?.MoveTo(_path.Nodes[_nodeIndex].Position);
    }
}
