using Godot;

namespace Jabroni.Nav;

public sealed class NavNode
{
    public Vector3 Position { get; }
    public double StayDuration { get; }

    public NavNode(Vector3 position, double stayDuration)
    {
        Position = position;
        StayDuration = stayDuration;
    }
}
