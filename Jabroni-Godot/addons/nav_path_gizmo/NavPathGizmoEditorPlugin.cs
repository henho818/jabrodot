using Godot;
using Jabroni.Nav;

namespace Jabroni.Editor;

/// <summary>
/// Registers NavPathGizmoPlugin so NavPath nodes get their ribbon drawn via Godot's
/// editor-gizmo API. Plugin boilerplate only -- the actual drawing lives in
/// scripts/Nav/NavPathGizmoPlugin.cs.
/// </summary>
[Tool]
public partial class NavPathGizmoEditorPlugin : EditorPlugin
{
	private NavPathGizmoPlugin _gizmoPlugin;

	public override void _EnterTree()
	{
		_gizmoPlugin = new NavPathGizmoPlugin();
		AddNode3DGizmoPlugin(_gizmoPlugin);
	}

	public override void _ExitTree()
	{
		RemoveNode3DGizmoPlugin(_gizmoPlugin);
		_gizmoPlugin = null;
	}
}
