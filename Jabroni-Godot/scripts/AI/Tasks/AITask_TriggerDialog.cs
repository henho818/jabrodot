using Jabroni.UI.Dialog;

namespace Jabroni.AI;

/// <summary>Opens the dialog box with the agent's configured dialog id and completes once it closes.</summary>
public sealed class AITask_TriggerDialog : AITask
{
    private bool _dialogStarted;

    public AITask_TriggerDialog(AgentAI agent) : base(agent)
    {
    }

    public override void Start()
    {
        var dialogBox = DialogBox.Instance;
        if (dialogBox == null || string.IsNullOrEmpty(Agent.Stats.ChatDialogId))
        {
            IsComplete = true;
            return;
        }

        _dialogStarted = true;
        dialogBox.Closed += OnDialogClosed;
        dialogBox.TriggerDialog(Agent.Stats.ChatDialogId);
    }

    // Mirrors the same DetectionRadius that sizes this agent's DetectionSphere (see
    // AgentAI._Ready) -- walking out of that sphere cancels the chat the same way it would
    // drop any other disturbance/vision detection.
    public override void Update(double delta)
    {
        if (!_dialogStarted || Agent.ChatTarget == null)
        {
            return;
        }

        float distance = Agent.Body.GlobalPosition.DistanceTo(Agent.ChatTarget.GlobalPosition);
        if (distance > Agent.Stats.DetectionRadius)
        {
            DialogBox.Instance?.Close();
        }
    }

    public override void End()
    {
        if (DialogBox.Instance != null)
        {
            DialogBox.Instance.Closed -= OnDialogClosed;
        }

        // Clear the other party's chat target too (e.g. the avatar's), so its own FSM
        // also leaves Chatting once this conversation ends -- otherwise only this
        // agent's side would know the chat is over.
        var other = Agent.ChatTarget?.GetNodeOrNull<AgentAI>("AgentAI");
        if (other != null)
        {
            other.ChatTarget = null;
        }

        Agent.ChatTarget = null;
    }

    private void OnDialogClosed()
    {
        IsComplete = true;
    }
}
