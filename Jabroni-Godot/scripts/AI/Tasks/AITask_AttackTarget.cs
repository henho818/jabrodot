using Godot;

namespace Jabroni.AI;

/// <summary>
/// Placeholder "attack": logs once and briefly flashes the agent's mesh red, then holds
/// facing the target indefinitely. There's no real combat/damage system yet -- this is a
/// visible, verifiable stand-in for "the shark caught you."
/// </summary>
public sealed class AITask_AttackTarget : AITask
{
    public AITask_AttackTarget(AgentAI agent) : base(agent)
    {
    }

    public override void Start()
    {
        // Search's chase task may have left an in-flight MoveTo destination active;
        // attacking should hold position, not keep coasting toward it (see the M6
        // patrol-interrupt fix for the same class of bug in AITask_FacePosition).
        Agent.Locomotion?.Stop();
        GD.Print($"[{Agent.Stats.Name}] caught {Agent.AttackTarget?.Name}!");
        FlashMesh();
    }

    public override void Update(double delta)
    {
        var target = Agent.AttackTarget;
        if (target != null)
        {
            Agent.Locomotion?.FaceWorldPosition(target.GlobalPosition);
        }
    }

    private void FlashMesh()
    {
        var mesh = Agent.Body.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        if (mesh == null || mesh.GetSurfaceOverrideMaterial(0) is not StandardMaterial3D material)
        {
            return;
        }

        var flashMaterial = (StandardMaterial3D)material.Duplicate();
        mesh.SetSurfaceOverrideMaterial(0, flashMaterial);
        Color original = flashMaterial.AlbedoColor;

        Tween tween = mesh.CreateTween();
        tween.TweenProperty(flashMaterial, "albedo_color", Colors.Red, 0.15);
        tween.TweenProperty(flashMaterial, "albedo_color", original, 0.15);
    }
}
