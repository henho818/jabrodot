using Godot;

namespace Jabroni.Triggers;

/// <summary>
/// What actually happens when an <see cref="AreaTrigger"/> fires. The trigger's Type dropdown
/// builds one of these for itself where a behaviour exists (Dialogue, Scene Load); this is the
/// route for everything else -- attach a concrete subclass to a node in the scene and drag that
/// node into the trigger's Handler slot, which is called in addition to the built-in one.
/// <para>
/// A Node3D rather than a Resource or a plain object because handlers usually care where they are:
/// the door they open, the camera anchor a cutscene starts from, the prop they animate. One handler
/// can serve many triggers -- it is handed a <see cref="TriggerEvent"/> naming the trigger, its
/// <see cref="TriggerType"/> and its payload, so a single "scene director" node can branch instead
/// of needing a handler per volume.
/// </para>
/// <para>
/// Subclasses override <see cref="OnTrigger"/>; overriding <see cref="Accepts"/> is how a handler
/// declines an event it isn't for (wrong type, wrong phase, already playing) without the trigger
/// needing to know. Anything that just wants to listen -- a quest log, an analytics hook -- can
/// connect to <see cref="HandledEventHandler"/> instead of subclassing.
/// </para>
/// </summary>
public abstract partial class TriggerHandler : Node3D
{
    /// <summary>Raised after a trigger event has been handled, for observers that aren't the handler.</summary>
    [Signal]
    public delegate void HandledEventHandler(TriggerEvent triggerEvent);

    /// <summary>Uncheck to make this handler ignore everything, without unwiring the triggers that point at it.</summary>
    [Export]
    public bool Enabled { get; set; } = true;

    /// <summary>Entry point used by <see cref="AreaTrigger"/>. Not virtual -- override <see cref="OnTrigger"/>.</summary>
    public void Handle(TriggerEvent triggerEvent)
    {
        if (!Enabled || triggerEvent == null || !Accepts(triggerEvent))
        {
            return;
        }

        OnTrigger(triggerEvent);
        EmitSignal(SignalName.Handled, triggerEvent);
    }

    /// <summary>Declines an event before <see cref="OnTrigger"/> runs. Accepts everything by default.</summary>
    protected virtual bool Accepts(TriggerEvent triggerEvent) => true;

    protected abstract void OnTrigger(TriggerEvent triggerEvent);
}
