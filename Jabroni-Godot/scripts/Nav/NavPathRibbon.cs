using System.Collections.Generic;
using Godot;

namespace Jabroni.Nav;

/// <summary>
/// Builds the camera-facing ribbon a NavPath is drawn as, shared by the editor gizmo
/// (NavPathGizmoPlugin) and the in-game debug renderer (NavPath.ShowInGame) so the path looks
/// the same in both and only has to be got right once.
///
/// Godot's line primitives have no width, so each segment is a quad widened toward the current
/// camera -- which is why this has to be rebuilt whenever the view moves, and why the caller
/// passes the camera in rather than this reaching for one.
/// </summary>
internal static class NavPathRibbon
{
	/// <summary>Lifts the ribbon clear of the ground so it doesn't z-fight the terrain.</summary>
	public const float LineHeight = 0.05f;

	/// <summary>Points are local to <paramref name="node"/>; widening happens in global space so
	/// the ribbon keeps its thickness however the node itself is scaled or rotated.</summary>
	public static ImmediateMesh Build(
		Node3D node, IReadOnlyList<Vector3> points, float thickness, Camera3D camera)
	{
		var mesh = new ImmediateMesh();
		float halfWidth = Mathf.Max(thickness, 0.001f) * 0.5f;

		mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i < points.Count - 1; i++)
		{
			Vector3 globalA = node.ToGlobal(points[i]);
			Vector3 globalB = node.ToGlobal(points[i + 1]);
			Vector3 direction = globalB - globalA;

			if (direction.LengthSquared() < 0.0001f)
			{
				continue;
			}

			direction = direction.Normalized();

			Vector3 perpendicular = default;
			if (camera != null)
			{
				Vector3 toCamera = camera.GlobalPosition - ((globalA + globalB) * 0.5f);
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

	/// <summary>The waypoint positions as ribbon points, local to the path and lifted clear of
	/// the ground, closing the loop when the path loops.</summary>
	public static List<Vector3> CollectPoints(NavPath path)
	{
		var points = new List<Vector3>();
		foreach (NavNode navNode in path.Nodes)
		{
			if (navNode != null)
			{
				points.Add(path.ToLocal(navNode.GlobalPosition) + (Vector3.Up * LineHeight));
			}
		}

		if (path.Looping && points.Count > 1)
		{
			points.Add(points[0]);
		}

		return points;
	}
}
