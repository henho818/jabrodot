using Godot;
using Jabroni.Core;

namespace Jabroni.AI;

/// <summary>
/// Polls for nearby agents each frame and reports sighted targets to the owning AgentAI.
/// Distinct from AgentDetectionSphere's coarser "something's nearby" disturbance signal --
/// this represents actually seeing a target well enough to act on (chase/attack). A sphere
/// is a simplification of the source project's vision cone; a prototype-fidelity stand-in,
/// not a true line-of-sight/field-of-view check.
/// </summary>
public partial class AgentVision : Area3D
{
    private CollisionShape3D _shape;
    private AgentAI _owner;

    public override void _Ready()
    {
        _shape = GetNode<CollisionShape3D>("CollisionShape3D");
    }

    public void Initialize(AgentAI owner, float radius)
    {
        _owner = owner;

        var sphere = _shape.Shape is SphereShape3D existing
            ? (SphereShape3D)existing.Duplicate()
            : new SphereShape3D();
        sphere.Radius = radius;
        _shape.Shape = sphere;
    }

    public override void _Process(double delta)
    {
        if (_owner == null)
        {
            return;
        }

        foreach (var body in GetOverlappingBodies())
        {
            if (body == _owner.Body)
            {
                continue;
            }

            if (body is CollisionObject3D co && (co.CollisionLayer & PhysicsLayers.Agent) != 0)
            {
                _owner.ReportTargetSighted(body);
                return;
            }
        }
    }
}
