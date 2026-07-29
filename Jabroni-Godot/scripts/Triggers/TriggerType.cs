namespace Jabroni.Triggers;

/// <summary>
/// What an <see cref="AreaTrigger"/> is for. Chosen from a dropdown in the Inspector and carried
/// on every <see cref="TriggerEvent"/>, so a handler shared by several triggers (a scene
/// director, say) can branch on it instead of needing one handler node per trigger.
/// <para>
/// It is deliberately descriptive, not dispatching: nothing in the framework maps a type to a
/// behaviour. What actually happens is whatever the assigned <see cref="TriggerHandler"/> does.
/// </para>
/// </summary>
public enum TriggerType
{
    Dialogue,
    Interaction,
    Cutscene,
    SceneLoad,
    Custom,
}
