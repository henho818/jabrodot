using Godot;
using Jabroni.Data;

namespace Jabroni.Editor;

/// <summary>
/// Swaps the plain text box Godot gives AgentAI.ChatDialogId for
/// <see cref="ChatDialogIdProperty"/>.
/// </summary>
[Tool]
public partial class DialogInspectorPlugin : EditorInspectorPlugin
{
    // Matched on the property name rather than on the edited object's type: AgentAI's subclasses
    // deliberately aren't [Tool], so while merely editing a scene the Inspector hands us the bare
    // native Node instead of the script type, and an `is AgentAI` test would never fire.
    public override bool _CanHandle(GodotObject @object)
    {
        return @object is Node;
    }

    public override bool _ParseProperty(
        GodotObject @object,
        Variant.Type type,
        string name,
        PropertyHint hintType,
        string hintString,
        PropertyUsageFlags usageFlags,
        bool wide)
    {
        if (type != Variant.Type.String || name != DialogSchema.ChatDialogIdProperty)
        {
            return false;
        }

        AddPropertyEditor(name, new ChatDialogIdProperty());
        return true;
    }
}
