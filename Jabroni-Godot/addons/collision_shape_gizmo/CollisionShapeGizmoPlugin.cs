using Godot;

namespace Jabroni.Editor;

/// <summary>
/// Draws every CollisionShape3D's shape clearly, regardless of type. Godot's own
/// Shape3D.GetDebugMesh() already builds a wireframe+fill ArrayMesh for whichever concrete
/// shape is assigned (Box, Capsule, Sphere, Cylinder, Convex, Concave, HeightMap, ...), so
/// there's no need to reconstruct geometry per shape type here -- just reuse that mesh and
/// swap in our own material.
///
/// That swap is the actual point: the engine hardcodes the fill surface's alpha as
/// debug_color * Color(1, 1, 1, 0.0625) inside get_debug_mesh() -- it force-multiplies
/// whatever alpha you set by ~1/16th, unconditionally, so the built-in fill can never
/// fully occlude the scene. AddMesh's material argument overrides every surface in the
/// mesh (both the wireframe and the fill), which bypasses that baked-in alpha entirely.
/// NoDepthTest keeps it visible through other geometry (walls, translucent water, etc.)
/// instead of getting lost in transparency-sorting order like the built-in gizmo can.
///
/// This draws *alongside* the native CollisionShape3D gizmo, not instead of it -- Godot
/// doesn't expose a way to unregister a built-in gizmo plugin.
///
/// Only draws for the currently-selected node -- Godot already calls UpdateGizmos() on a
/// node whenever its selection state changes (the same mechanism that shows/hides the
/// built-in move/rotate handles), so re-checking EditorSelection inside _Redraw is enough;
/// no extra signal wiring needed.
///
/// Color and visibility follow the node's own DebugColor/DebugFill/Disabled -- same fields
/// the native gizmo already reads -- rather than a fixed color, so this stays consistent
/// with whatever each shape is individually configured to show (or not show).
/// </summary>
[Tool]
public partial class CollisionShapeGizmoPlugin : EditorNode3DGizmoPlugin
{
	private readonly StandardMaterial3D _material = new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		NoDepthTest = true,
	};

	public override string _GetGizmoName() => "CollisionShapeDebug";

	public override bool _HasGizmo(Node3D forNode) => forNode is CollisionShape3D;

	public override void _Redraw(EditorNode3DGizmo gizmo)
	{
		gizmo.Clear();

		if (gizmo.GetNode3D() is not CollisionShape3D { Disabled: false, DebugFill: true } collisionShape || collisionShape.Shape == null)
		{
			return;
		}

		if (!EditorInterface.Singleton.GetSelection().GetSelectedNodes().Contains(collisionShape))
		{
			return;
		}

		_material.AlbedoColor = collisionShape.DebugColor;
		gizmo.AddMesh(collisionShape.Shape.GetDebugMesh(), _material);
	}
}
