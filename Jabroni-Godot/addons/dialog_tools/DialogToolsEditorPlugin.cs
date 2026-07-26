using Godot;

namespace Jabroni.Editor;

/// <summary>
/// Adds the "Dialogue" dock, defaulting to the bottom slot because the graph view wants the full
/// editor width. Plugin boilerplate only -- the validation and graph view live in
/// DialogToolsPanel/DialogGraphView.
/// </summary>
[Tool]
public partial class DialogToolsEditorPlugin : EditorPlugin
{
    private EditorDock _dock;

    public override void _EnterTree()
    {
        _dock = new EditorDock
        {
            Name = "DialogueDock",
            Title = "Dialogue",
            DefaultSlot = EditorDock.DockSlot.Bottom,
        };
        _dock.AddChild(new DialogToolsPanel { Name = "DialogTools" });

        AddDock(_dock);
    }

    public override void _ExitTree()
    {
        RemoveDock(_dock);
        _dock.QueueFree();
        _dock = null;
    }
}
