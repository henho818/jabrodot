using Godot;

namespace Jabroni.World;

internal static class LocomotionMath
{
    public static Basis TurnToward(Basis current, Vector3 direction, float rotationSpeed, float dt)
    {
        Basis targetBasis = Basis.LookingAt(direction, Vector3.Up);
        return current.Slerp(targetBasis, rotationSpeed * dt);
    }
}
