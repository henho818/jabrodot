using Godot;

namespace Jabroni.Nav;

/// <summary>
/// Draws a blue-to-red gradient along every NavigationLink3D, so the direction of a link is
/// readable at a glance: blue where an agent enters, red where it comes out. Registered by
/// NavLinkGizmoEditorPlugin (addons/nav_link_gizmo).
///
/// The link's StartPosition/EndPosition are relative to the link node's own transform, and
/// EditorNode3DGizmo also draws in its owner's local space and applies that transform itself,
/// so the properties go straight into AddLines -- converting to global first would apply the
/// transform twice.
///
/// AddLines paints a whole batch in a single Material with no per-vertex colour, so the fade
/// is drawn as BandCount straight segments, each in its own material. Those are created once
/// in the constructor rather than per redraw: unlike patrol paths, which are few and short,
/// there can be a link at every ledge in the level and each one redraws on any camera move.
///
/// AddLines also has no width control, so -- as NavPathGizmoPlugin does for its ribbon -- each
/// band is a quad widened toward the editor camera and handed to AddMesh instead. The width
/// comes from an editor setting rather than an [Export], because NavigationLink3D is a built-in
/// class and can't carry one; that also makes it a single knob for every link at once.
///
/// Godot draws its own gizmo for NavigationLink3D as well, including the two draggable
/// endpoint handles. This is an overlay on top of that, not a replacement for it -- the
/// handles still belong to the built-in gizmo.
/// </summary>
[Tool]
public partial class NavLinkGizmoPlugin : EditorNode3DGizmoPlugin
{
	/// <summary>Segments the line is split into to fake a gradient. Enough to read as a fade
	/// without turning one link into a pile of draw calls.</summary>
	private const int BandCount = 12;

	/// <summary>Editor setting holding the drawn width, in metres. Lives under the same
	/// prefix Godot uses for its own 3D gizmo appearance settings.</summary>
	public const string LineWidthSetting = "editors/3d_gizmos/nav_link/line_width";

	public const float DefaultLineWidth = 0.06f;

	private static readonly Color StartColor = new(0.2f, 0.45f, 1f);
	private static readonly Color EndColor = new(1f, 0.2f, 0.15f);

	public NavLinkGizmoPlugin()
	{
		for (int i = 0; i < BandCount; i++)
		{
			float t = BandCount == 1 ? 0f : i / (float)(BandCount - 1);
			CreateMaterial(BandName(i), StartColor.Lerp(EndColor, t));
		}
	}

	public override string _GetGizmoName() => nameof(NavigationLink3D);

	public override bool _HasGizmo(Node3D forNode) => forNode is NavigationLink3D;

	public override void _Redraw(EditorNode3DGizmo gizmo)
	{
		gizmo.Clear();

		if (gizmo.GetNode3D() is not NavigationLink3D link)
		{
			return;
		}

		Vector3 start = link.StartPosition;
		Vector3 end = link.EndPosition;

		if (start.IsEqualApprox(end))
		{
			// A zero-length link connects nothing; drawing it would just be a dot at the origin.
			return;
		}

		float width = ReadLineWidth();
		Camera3D camera = EditorInterface.Singleton?.GetEditorViewport3D(0)?.GetCamera3D();

		for (int i = 0; i < BandCount; i++)
		{
			Vector3 from = start.Lerp(end, i / (float)BandCount);
			Vector3 to = start.Lerp(end, (i + 1) / (float)BandCount);
			gizmo.AddMesh(BuildSegment(link, from, to, width, camera), GetMaterial(BandName(i), gizmo));
		}
	}

	private static float ReadLineWidth()
	{
		EditorSettings settings = EditorInterface.Singleton?.GetEditorSettings();
		if (settings == null || !settings.HasSetting(LineWidthSetting))
		{
			return DefaultLineWidth;
		}

		return (float)settings.GetSetting(LineWidthSetting);
	}

	/// <summary>
	/// One band as a quad turned to face the editor camera, since a gizmo line has no width.
	/// Widening happens in global space so the ribbon keeps its thickness regardless of how the
	/// link node itself is scaled or rotated, then comes back to local for the mesh.
	/// </summary>
	private static ImmediateMesh BuildSegment(
		Node3D node, Vector3 from, Vector3 to, float width, Camera3D camera)
	{
		var mesh = new ImmediateMesh();
		float halfWidth = Mathf.Max(width, 0.001f) * 0.5f;

		Vector3 globalFrom = node.ToGlobal(from);
		Vector3 globalTo = node.ToGlobal(to);
		Vector3 direction = globalTo - globalFrom;

		if (direction.LengthSquared() < 0.0001f)
		{
			return mesh;
		}

		direction = direction.Normalized();

		Vector3 perpendicular = default;
		if (camera != null)
		{
			Vector3 toCamera = camera.GlobalPosition - ((globalFrom + globalTo) * 0.5f);
			perpendicular = direction.Cross(toCamera);
		}

		// Camera missing, or the segment points straight at it: fall back to a horizontal
		// perpendicular so the band still renders as something.
		if (perpendicular.LengthSquared() < 0.0001f)
		{
			perpendicular = new Vector3(-direction.Z, 0f, direction.X);
		}

		perpendicular = perpendicular.Normalized() * halfWidth;

		Vector3 a0 = node.ToLocal(globalFrom - perpendicular);
		Vector3 a1 = node.ToLocal(globalFrom + perpendicular);
		Vector3 b0 = node.ToLocal(globalTo - perpendicular);
		Vector3 b1 = node.ToLocal(globalTo + perpendicular);

		mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
		mesh.SurfaceAddVertex(a0);
		mesh.SurfaceAddVertex(a1);
		mesh.SurfaceAddVertex(b1);
		mesh.SurfaceAddVertex(a0);
		mesh.SurfaceAddVertex(b1);
		mesh.SurfaceAddVertex(b0);
		mesh.SurfaceEnd();

		return mesh;
	}

	private static string BandName(int index) => $"nav_link_band_{index}";
}
