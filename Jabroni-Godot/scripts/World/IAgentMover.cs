using Godot;

namespace Jabroni.World;

/// <summary>
/// Movement contract shared by every locomotion style (NavMesh-pathed vs. straight-line
/// "swimming" translation), so AI tasks/conditions never need to branch on agent type --
/// they just call MoveTo/FaceWorldPosition/Stop and read HasArrived.
/// </summary>
public interface IAgentMover
{
    bool HasArrived { get; }
    float Speed { get; set; }
    void MoveTo(Vector3 destination);
    void FaceWorldPosition(Vector3 position);
    void Stop();
}
