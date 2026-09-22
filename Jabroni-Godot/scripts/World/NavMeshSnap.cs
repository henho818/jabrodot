using Godot;

namespace Jabroni.World;

/// <summary>
/// Projects arbitrary world points onto the navigation mesh, so callers can only ever hand
/// the navigation server somewhere an agent is actually able to stand.
///
/// NavigationServer3D reports a miss as Vector3.Zero, which is indistinguishable from a
/// genuine result at the world origin, so nothing here trusts that sentinel: a segment query
/// is checked against the segment it was cast along, and callers get an explicit bool.
/// <see cref="NoPoint"/> is offered for the places that want a sentinel rather than a
/// try-pattern -- being float.MinValue on every axis, it can't collide with a real coordinate.
/// </summary>
public static class NavMeshSnap
{
    public static readonly Vector3 NoPoint = new(float.MinValue, float.MinValue, float.MinValue);

    /// <summary>Obstacle tops (roofs, walls, hedges) bake into the navmesh as unreachable
    /// floating islands, and closest-point is a 3D query -- so a point beside a building can
    /// project up onto its roof. A projection that climbs further than this is one of those.</summary>
    private const float MaxRise = 1.5f;

    /// <summary>A projection further than this from the request isn't a correction any more,
    /// and usually means the map is empty and returned the origin.</summary>
    private const float MaxDistance = 8f;

    /// <summary>An intersection returned by a segment query lies on that segment; anything
    /// off it is the miss sentinel rather than a hit.</summary>
    private const float OnSegmentEpsilon = 0.01f;

    /// <summary>True once the map has completed a synchronization and can be queried. Before
    /// that every query logs an error and returns nothing useful.</summary>
    public static bool IsReady(Rid map)
    {
        return map.IsValid && NavigationServer3D.MapGetIterationId(map) != 0;
    }

    /// <summary>Projects <paramref name="point"/> onto the nearest navmesh surface.</summary>
    public static bool TryProject(Rid map, Vector3 point, out Vector3 result)
    {
        result = NoPoint;

        if (!IsReady(map))
        {
            return false;
        }

        Vector3 closest = NavigationServer3D.MapGetClosestPoint(map, point);

        if (Mathf.Abs(closest.Y - point.Y) > MaxRise
            || closest.DistanceSquaredTo(point) > MaxDistance * MaxDistance)
        {
            return false;
        }

        result = closest;
        return true;
    }

    /// <summary>Casts a segment against the navmesh surface itself -- no physics involved --
    /// so a hit is walkable by construction.</summary>
    public static bool TryRaycast(Rid map, Vector3 from, Vector3 to, out Vector3 result)
    {
        result = NoPoint;

        if (!IsReady(map))
        {
            return false;
        }

        Vector3 hit = NavigationServer3D.MapGetClosestPointToSegment(map, from, to, true);

        if (DistanceToSegment(hit, from, to) > OnSegmentEpsilon)
        {
            return false;
        }

        result = hit;
        return true;
    }

    private static float DistanceToSegment(Vector3 point, Vector3 from, Vector3 to)
    {
        Vector3 span = to - from;
        float lengthSquared = span.LengthSquared();

        if (lengthSquared < 0.0001f)
        {
            return point.DistanceTo(from);
        }

        float t = Mathf.Clamp((point - from).Dot(span) / lengthSquared, 0f, 1f);
        return point.DistanceTo(from + span * t);
    }
}
