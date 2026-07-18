using Godot;

namespace Jabroni.Editor;

/// <summary>
/// Registers CollisionShapeGizmoPlugin so every CollisionShape3D gets a clearly-visible
/// debug overlay. Plugin boilerplate only -- the actual drawing lives in
/// CollisionShapeGizmoPlugin.cs.
/// </summary>
[Tool]
public partial class CollisionShapeGizmoEditorPlugin : EditorPlugin
{
	private CollisionShapeGizmoPlugin _gizmoPlugin;

	public override void _EnterTree()
	{
		_gizmoPlugin = new CollisionShapeGizmoPlugin();
		AddNode3DGizmoPlugin(_gizmoPlugin);
	}

	public override void _ExitTree()
	{
		RemoveNode3DGizmoPlugin(_gizmoPlugin);
		_gizmoPlugin = null;
	}
}
