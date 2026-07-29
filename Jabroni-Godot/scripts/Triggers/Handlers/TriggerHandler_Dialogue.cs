using Godot;
using Jabroni.UI.Dialog;

namespace Jabroni.Triggers.Handlers;

/// <summary>
/// Opens the dialog box on the authored Dialog row -- the walk-into-it counterpart to
/// AITask_TriggerDialog, which is how an agent you *talk to* opens the same box. Nothing here waits
/// for the box to close; the trigger's OneShot/ReArmSeconds decide whether it can fire again.
/// <para>
/// The ChatDialogId field gets the Inspector's Dialog picker (dropdown of the ids that exist, red
/// when what's typed isn't one of them) for free: DialogInspectorPlugin matches on the property
/// name, not the node type.
/// </para>
/// </summary>
public partial class TriggerHandler_Dialogue : TriggerHandler
{
    /// <summary>The Dialog row to open. Empty means this handler does nothing.</summary>
    [Export]
    public string ChatDialogId { get; set; } = "";

    /// <summary>Skips the event if a dialog is already on screen, rather than cutting it off mid-line.</summary>
    [Export]
    public bool SkipWhileDialogOpen { get; set; } = true;

    protected override bool Accepts(TriggerEvent triggerEvent)
    {
        if (string.IsNullOrEmpty(ChatDialogId))
        {
            return false;
        }

        var dialogBox = DialogBox.Instance;
        if (dialogBox == null)
        {
            GD.PushWarning($"{Name}: no DialogBox in the scene -- '{ChatDialogId}' not opened.");
            return false;
        }

        return !(SkipWhileDialogOpen && dialogBox.Visible);
    }

    protected override void OnTrigger(TriggerEvent triggerEvent)
    {
        DialogBox.Instance.TriggerDialog(ChatDialogId);
    }
}
