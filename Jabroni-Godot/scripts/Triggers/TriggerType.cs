namespace Jabroni.Triggers;

/// <summary>
/// What an <see cref="AreaTrigger"/> is for. Chosen from a dropdown in the Inspector and carried
/// on every <see cref="TriggerEvent"/>, so a handler shared by several triggers (a scene
/// director, say) can branch on it instead of needing one handler node per trigger.
/// <para>
/// It dispatches only where there is already a system to dispatch to: picking Dialogue or SceneLoad
/// reveals that behaviour's one field on the trigger and runs the matching
/// <see cref="TriggerHandler"/> for it, so the common volumes need no second node. The rest are
/// descriptive only -- they say what the volume is for, and an assigned handler or a listener on
/// the trigger's Fired signal decides what that means. Either way an assigned
/// <see cref="TriggerHandler"/> is called as well, never instead.
/// </para>
/// </summary>
public enum TriggerType
{
    /// <summary>Opens the dialog box on the trigger's ChatDialogId.</summary>
    Dialogue,

    Interaction,
    Cutscene,

    /// <summary>Swaps the running scene for the trigger's ScenePath.</summary>
    SceneLoad,

    Custom,
}
