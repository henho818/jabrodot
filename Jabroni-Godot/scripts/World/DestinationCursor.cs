using Godot;

namespace Jabroni.World;

/// <summary>A short-lived ground marker spawned where the player clicked to move.</summary>
public partial class DestinationCursor : MeshInstance3D
{
    [Export] public float Lifetime { get; set; } = 0.6f;

    public override void _Ready()
    {
        var material = (StandardMaterial3D)GetSurfaceOverrideMaterial(0)?.Duplicate();
        if (material != null)
        {
            SetSurfaceOverrideMaterial(0, material);
        }

        Vector3 startScale = Scale;
        Color startColor = material?.AlbedoColor ?? Colors.White;

        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", startScale * 1.4f, Lifetime).SetTrans(Tween.TransitionType.Sine);
        if (material != null)
        {
            tween.TweenProperty(material, "albedo_color", new Color(startColor.R, startColor.G, startColor.B, 0f), Lifetime)
                .SetTrans(Tween.TransitionType.Sine);
        }

        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }
}
