using Godot;

namespace Jabroni.Triggers.Handlers;

/// <summary>
/// Does nothing itself -- it just re-emits the event as its Handled signal, so anything in the
/// scene can connect to it from the Node dock without a new handler class being written. The escape
/// hatch for the trigger types that have no dedicated handler yet (Interaction, Cutscene, Custom):
/// wire the volume up now, and swap in a real handler once the system behind it exists.
/// <para>
/// Optionally prints each event, which is the fastest way to confirm a freshly-placed volume is
/// actually where you think it is.
/// </para>
/// </summary>
public partial class TriggerHandler_Relay : TriggerHandler
{
    [Export]
    public bool PrintToOutput { get; set; }

    protected override void OnTrigger(TriggerEvent triggerEvent)
    {
        if (PrintToOutput)
        {
            GD.Print($"[Trigger] {triggerEvent}");
        }
    }
}
