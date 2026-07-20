using Godot;

namespace Jabroni.Nav;

/// <summary>
/// A single patrol waypoint, authored visually as a child of a NavPath node (drag it in the
/// 3D viewport, reorder in the Scene tree). Position comes from the node itself; StayDuration
/// is how long an agent waits here before moving to the next waypoint.
/// </summary>
[Tool]
public partial class NavNode : Marker3D
{
	/// <summary>
	/// Snapped-to-terrain world position, set by NavPathTerrainSnapper. Distinct from the
	/// inherited Position (local, author-time authoring value) since locomotion needs the
	/// resolved world target.
	/// </summary>
	public Vector3 WorldPosition => GlobalPosition;

	[Export(PropertyHint.Range, "0,30,0.1,or_greater")]
	public double StayDuration { get; set; } = 1.5;
}
