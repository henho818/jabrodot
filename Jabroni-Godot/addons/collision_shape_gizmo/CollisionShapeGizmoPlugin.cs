using System.Collections.Generic;
using Godot;
using Jabroni.Triggers;

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
/// For the selected shape it also drops depth testing, so the whole of what you are editing
/// stays visible through the geometry it is embedded in instead of getting lost in
/// transparency-sorting order like the built-in gizmo can.
///
/// This draws *alongside* the native CollisionShape3D gizmo, not instead of it -- Godot
/// doesn't expose a way to unregister a built-in gizmo plugin.
///
/// Ordinary colliders draw only while selected -- Godot already calls UpdateGizmos() on a
/// node whenever its selection state changes (the same mechanism that shows/hides the
/// built-in move/rotate handles), so re-checking EditorSelection inside _Redraw is enough;
/// no extra signal wiring needed. An <see cref="AreaTrigger"/>'s shapes are the exception
/// and stay filled in permanently: a trigger volume has no mesh of its own to be seen by,
/// and the whole point of placing one is knowing where its edge sits relative to the
/// scenery around it. AreaTrigger.ShowInEditor turns that back off per volume.
///
/// Color and visibility follow the node's own DebugColor/DebugFill/Disabled -- same fields
/// the native gizmo already reads -- rather than a fixed color, so this stays consistent
/// with whatever each shape is individually configured to show (or not show).
/// </summary>
[Tool]
public partial class CollisionShapeGizmoPlugin : EditorNode3DGizmoPlugin
{
	// One material per (colour, x-ray) pair rather than one shared material whose AlbedoColor is
	// rewritten on every redraw: AddMesh only stores a reference, so now that several shapes can be
	// drawn at once (unselected trigger volumes), the last redraw's settings would otherwise win for
	// all of them. Bounded in practice by how many distinct debug colours a scene actually uses.
	private readonly Dictionary<(Color Color, bool XRay), StandardMaterial3D> _materials = new();

	public override string _GetGizmoName() => "CollisionShapeDebug";

	public override bool _HasGizmo(Node3D forNode) => forNode is CollisionShape3D;

	public override void _Redraw(EditorNode3DGizmo gizmo)
	{
		gizmo.Clear();

		if (gizmo.GetNode3D() is not CollisionShape3D { Disabled: false, DebugFill: true } collisionShape || collisionShape.Shape == null)
		{
			return;
		}

		bool selected = EditorInterface.Singleton.GetSelection().GetSelectedNodes().Contains(collisionShape);
		if (!selected && !DrawsUnselected(collisionShape))
		{
			return;
		}

		gizmo.AddMesh(collisionShape.Shape.GetDebugMesh(), MaterialFor(collisionShape.DebugColor, xRay: selected));
	}

	private static bool DrawsUnselected(CollisionShape3D collisionShape) =>
		collisionShape.GetParent() is AreaTrigger { ShowInEditor: true };

	/// <summary>
	/// X-ray (NoDepthTest) is for the shape you are working on: you want to see the whole of it,
	/// through the wall it is embedded in. A trigger volume that is merely *shown* is depth-tested
	/// instead, so it reads as a transparent box standing in the scene rather than a decal floating
	/// over everything -- otherwise a volume on the far side of the village would draw on top of it.
	/// </summary>
	private StandardMaterial3D MaterialFor(Color color, bool xRay)
	{
		if (_materials.TryGetValue((color, xRay), out var material))
		{
			return material;
		}

		material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			NoDepthTest = xRay,
			AlbedoColor = color,
		};

		_materials[(color, xRay)] = material;
		return material;
	}
}
