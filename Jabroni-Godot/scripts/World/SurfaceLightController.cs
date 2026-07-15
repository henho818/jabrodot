using Godot;

namespace Jabroni.World;

/// <summary>
/// Continuously rotates around its own local origin, carrying a child light in a circle --
/// reproduces the source project's SurfaceLightController (a light orbiting to simulate
/// shifting underwater caustics/sunbeams). Attach to a pivot Node3D positioned at the
/// water area's center, with the actual Light3D as a child offset along local X.
/// </summary>
public partial class SurfaceLightController : Node3D
{
    [Export] public float RotateDegreesPerSecond { get; set; } = 12f;

    public override void _Process(double delta)
    {
        RotateY(Mathf.DegToRad(RotateDegreesPerSecond) * (float)delta);
    }
}
