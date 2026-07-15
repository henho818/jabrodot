using Godot;

namespace Jabroni.World;

/// <summary>
/// Bakes the navmesh at runtime from this region's child MeshInstance3D geometry
/// (ground + obstacles), since there is no editor session available to bake and save
/// a NavigationMesh resource ahead of time.
/// </summary>
public partial class VillageNavigation : NavigationRegion3D
{
    public override void _Ready()
    {
        BakeNavigationMesh(false);
    }
}
