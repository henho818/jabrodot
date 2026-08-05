using Godot;
using Jabroni.UI.Dialog;

namespace Jabroni.Triggers.Handlers;

/// <summary>
/// Opens the dialog box on the authored Dialog row -- the walk-into-it counterpart to
/// AITask_TriggerDialog, which is how an agent you *talk to* opens the same box. Nothing here waits
/// for the box to close; the trigger's OneShot/ReArmSeconds decide whether it can fire again.
/// <para>
/// This is what an <see cref="AreaTrigger"/> set to Type = Dialogue builds for itself, so most
/// volumes never need one placed by hand. Add one as a node when a single conversation should also
/// be openable from somewhere that isn't that volume, or when several triggers share it.
/// </para>
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

    /// <summary>
    /// Halts whoever walked in, so the box opens over a character standing still rather than one
    /// striding on across the scene while the narrator talks. Uncheck for a volume that should
    /// narrate over movement -- a line that plays as you cross a bridge, say.
    /// </summary>
    [Export]
    public bool StopInitiator { get; set; } = true;

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
        if (StopInitiator)
        {
            StopWalking(triggerEvent);
        }

        DialogBox.Instance.TriggerDialog(ChatDialogId);
    }

    /// <summary>
    /// Clears the chat target as well as calling Stop(), which is what makes this a real halt rather
    /// than a stutter: an avatar mid-approach to an NPC would otherwise satisfy HasArrived the moment
    /// its destination became its own position, and the state machine would walk it straight from
    /// NavToAgent into Chatting -- opening that NPC's conversation on top of the one just triggered.
    /// Clearing the target first sends it to Idle instead. Same reason AvatarAgentAI.MoveToGround
    /// clears it before every click-to-move.
    /// </summary>
    private static void StopWalking(TriggerEvent triggerEvent)
    {
        var agent = triggerEvent.InitiatorAgent;
        if (agent == null)
        {
            return;
        }

        agent.ChatTarget = null;
        agent.Locomotion?.Stop();
    }
}
