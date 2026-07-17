using System.Collections.Generic;
using System.Linq;
using Godot;
using Jabroni.Core;

namespace Jabroni.Nav;

/// <summary>
/// Builds a NavPath from a "PatrolPath" child node's Marker3D children (authored visually
/// in the editor -- drag them in the viewport, reorder in the Scene tree). When
/// snapToTerrain is true, each waypoint's Y is snapped to the nearest Pathable surface via
/// a vertical raycast at load time, so waypoints can be placed loosely (under the terrain,
/// floating above it) without matching its exact height -- useful since the terrain is
/// still subject to change. Terrain collision (Terrain3D) isn't guaranteed to be queryable
/// on the first physics frame, so Build() returns null until every waypoint resolves a
/// hit; callers should retry from _PhysicsProcess. Ground-agnostic movers (e.g. a
/// swimming agent that holds a fixed depth) should pass snapToTerrain: false and use the
/// waypoint's authored Y directly.
/// </summary>
public static class PatrolPathBuilder
{
    private const float RayHalfHeight = 500f;

    public static NavPath Build(Node3D body, string childName, double defaultStayDuration, bool looping, bool snapToTerrain)
    {
        var pathNode = body.GetNodeOrNull<Node3D>(childName);
        if (pathNode == null)
        {
            return null;
        }

        var waypoints = pathNode.GetChildren().OfType<Marker3D>().ToList();
        if (waypoints.Count == 0)
        {
            return null;
        }

        var spaceState = body.GetWorld3D().DirectSpaceState;
        var nodes = new List<NavNode>(waypoints.Count);

        foreach (var waypoint in waypoints)
        {
            var position = waypoint.GlobalPosition;

            if (snapToTerrain && !TrySnapToSurface(spaceState, position, out position))
            {
                return null;
            }

            double stayDuration = waypoint.HasMeta("StayDuration")
                ? waypoint.GetMeta("StayDuration").AsDouble()
                : defaultStayDuration;

            nodes.Add(new NavNode(position, stayDuration));
        }

        return new NavPath(nodes, looping);
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
