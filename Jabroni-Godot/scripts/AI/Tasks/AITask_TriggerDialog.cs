using Jabroni.UI.Dialog;

namespace Jabroni.AI;

/// <summary>Opens the dialog box with the agent's configured dialog id and completes once it closes.</summary>
public sealed class AITask_TriggerDialog : AITask
{
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

        dialogBox.Closed += OnDialogClosed;
        dialogBox.TriggerDialog(Agent.Stats.ChatDialogId);
    }

    public override void End()
    {
        if (DialogBox.Instance != null)
        {
            DialogBox.Instance.Closed -= OnDialogClosed;
        }

        Agent.ChatTarget = null;
    }

    private void OnDialogClosed()
    {
        IsComplete = true;
    }
}
