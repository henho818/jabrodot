namespace Jabroni.Core;

/// <summary>Named bits for the 3D physics layers used across the project (see also project.godot layer_names).</summary>
public static class PhysicsLayers
{
    public const uint Pathable = 1 << 0;
    public const uint Obstacle = 1 << 1;
    public const uint Agent = 1 << 2;
}
