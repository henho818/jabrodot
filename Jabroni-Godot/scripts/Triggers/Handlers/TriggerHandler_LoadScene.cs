using Godot;

namespace Jabroni.Triggers.Handlers;

/// <summary>
/// Swaps the running scene for another one -- a cave mouth, a doorway, a level exit.
/// <para>
/// The swap is deferred: this runs inside the physics server's overlap callback, and freeing the
/// scene tree from in there is exactly what ChangeSceneToFile's own "deferred" contract exists to
/// avoid. Pair this with a OneShot trigger -- a re-armable one could queue a second load while the
/// first is still pending.
/// </para>
/// </summary>
public partial class TriggerHandler_LoadScene : TriggerHandler
{
    /// <summary>The .tscn to load. Picked from the filesystem in the Inspector.</summary>
    [Export(PropertyHint.File, "*.tscn")]
    public string ScenePath { get; set; } = "";

    protected override bool Accepts(TriggerEvent triggerEvent)
    {
        if (string.IsNullOrEmpty(ScenePath))
        {
            return false;
        }

        if (!ResourceLoader.Exists(ScenePath))
        {
            GD.PushWarning($"{Name}: scene '{ScenePath}' does not exist -- not loading.");
            return false;
        }

        return true;
    }

    protected override void OnTrigger(TriggerEvent triggerEvent)
    {
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, ScenePath);
    }
}
