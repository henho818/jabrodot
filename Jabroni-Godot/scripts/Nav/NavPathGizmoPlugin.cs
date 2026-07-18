using System.Collections.Generic;
using Godot;

namespace Jabroni.Nav;

/// <summary>
/// Draws the ribbon for every NavPath node using Godot's editor-gizmo API
/// (EditorNode3DGizmo) instead of a manually managed child MeshInstance3D. Registered by
/// NavPathGizmoEditorPlugin (addons/nav_path_gizmo).
///
/// Godot's native gizmo AddLines() draws camera-facing lines automatically but has no
/// width control, so to keep LineThickness meaningful this still builds a small ribbon
/// mesh by hand -- widening each segment toward the current editor camera -- and hands it
/// to AddMesh() instead. The material disables back-face culling since the ribbon's
/// winding isn't guaranteed to face the camera from every angle.
///
/// Walks NavNode children directly with GetChildren() rather than the parent's cached
/// Nodes property, since that cache is meant for runtime (populated once, after which
/// NavNode children don't change) and would go stale while authoring a path in the editor.
/// </summary>
[Tool]
public partial class NavPathGizmoPlugin : EditorNode3DGizmoPlugin
{
	private const float LineHeight = 0.05f;

	private readonly StandardMaterial3D _material = new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};

	public override string _GetGizmoName() => nameof(NavPath);

	public override bool _HasGizmo(Node3D forNode) => forNode is NavPath;

	public override void _Redraw(EditorNode3DGizmo gizmo)
	{
		gizmo.Clear();

		if (gizmo.GetNode3D() is not NavPath path)
		{
			return;
		}

		var points = new List<Vector3>();
		foreach (var child in path.GetChildren())
		{
			if (child is NavNode node)
			{
				points.Add(node.Position + Vector3.Up * LineHeight);
			}
		}

		if (points.Count < 2)
		{
			return;
		}

		if (path.Looping)
		{
			points.Add(points[0]);
		}

		Camera3D camera = EditorInterface.Singleton?.GetEditorViewport3D(0)?.GetCamera3D();
		_material.AlbedoColor = path.LineColor;

		gizmo.AddMesh(BuildRibbon(path, points, path.LineThickness, camera), _material);
	}

	// Widens each segment toward the current editor camera (rather than a fixed world-up
	// cross product) so the ribbon reads as a line from any viewing angle -- a flat,
	// ground-plane quad goes edge-on and disappears when looking down at the terrain,
	// which is the normal top-down editor view.
	private static ImmediateMesh BuildRibbon(Node3D node, List<Vector3> points, float thickness, Camera3D camera)
	{
		var mesh = new ImmediateMesh();
		float halfWidth = Mathf.Max(thickness, 0.001f) * 0.5f;

		mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i < points.Count - 1; i++)
		{
			Vector3 a = points[i];
			Vector3 b = points[i + 1];
			Vector3 globalA = node.ToGlobal(a);
			Vector3 globalB = node.ToGlobal(b);
			Vector3 direction = globalB - globalA;

			if (direction.LengthSquared() < 0.0001f)
			{
				continue;
			}

			direction = direction.Normalized();

			Vector3 perpendicular = default;
			if (camera != null)
			{
				Vector3 toCamera = camera.GlobalPosition - (globalA + globalB) * 0.5f;
				perpendicular = direction.Cross(toCamera);
			}

			// Camera missing, or the segment happens to point straight at it: fall back to
			// a horizontal perpendicular so the ribbon still renders as something.
			if (perpendicular.LengthSquared() < 0.0001f)
			{
				perpendicular = new Vector3(-direction.Z, 0f, direction.X);
			}

			perpendicular = perpendicular.Normalized() * halfWidth;

			Vector3 a0 = node.ToLocal(globalA - perpendicular);
			Vector3 a1 = node.ToLocal(globalA + perpendicular);
			Vector3 b0 = node.ToLocal(globalB - perpendicular);
			Vector3 b1 = node.ToLocal(globalB + perpendicular);

			mesh.SurfaceAddVertex(a0);
			mesh.SurfaceAddVertex(a1);
			mesh.SurfaceAddVertex(b1);

			mesh.SurfaceAddVertex(a0);
			mesh.SurfaceAddVertex(b1);
			mesh.SurfaceAddVertex(b0);
		}

		mesh.SurfaceEnd();
		return mesh;
	}
}
