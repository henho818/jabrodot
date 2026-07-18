using Godot;
using Jabroni.Core;

namespace Jabroni.Nav;

/// <summary>
/// Snaps each NavNode child of a NavPath to the nearest Pathable surface via a vertical
/// raycast, in place -- so waypoints can be authored loosely (under the terrain, floating
/// above it) without matching its exact height, useful since the terrain is still subject
/// to change. Terrain3D collision isn't guaranteed to be queryable on the first physics
/// frame, so TrySnap returns false until every waypoint resolves a hit; callers should
/// retry from _PhysicsProcess. Ground-agnostic movers (e.g. a swimming agent that holds a
/// fixed depth) should skip calling this and use the NavPath's authored positions directly.
/// </summary>
public static class NavPathTerrainSnapper
{
	private const float RayHalfHeight = 500f;

	public static bool TrySnap(NavPath path)
	{
		var spaceState = path.GetWorld3D().DirectSpaceState;

		foreach (var node in path.Nodes)
		{
			if (!TrySnapToSurface(spaceState, node.GlobalPosition, out var position))
			{
				return false;
			}

			node.GlobalPosition = position;
		}

		return true;
	}

	private static bool TrySnapToSurface(PhysicsDirectSpaceState3D spaceState, Vector3 position, out Vector3 result)
	{
		var from = new Vector3(position.X, position.Y + RayHalfHeight, position.Z);
		var to = new Vector3(position.X, position.Y - RayHalfHeight, position.Z);
		var query = PhysicsRayQueryParameters3D.Create(from, to, PhysicsLayers.Pathable);
		var hit = spaceState.IntersectRay(query);

		if (hit.Count == 0)
		{
			result = position;
			return false;
		}

		result = (Vector3)hit["position"];
		return true;
	}
}
