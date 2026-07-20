using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Jabroni.Nav;

/// <summary>
/// Draws the ribbon for every NavPath node, plus a waypoint-index label at each one, using
/// Godot's editor-gizmo API (EditorNode3DGizmo) instead of a manually managed child
/// MeshInstance3D. Registered by NavPathGizmoEditorPlugin (addons/nav_path_gizmo).
///
/// Godot's native gizmo AddLines() draws camera-facing lines automatically but has no
/// width control, so to keep LineThickness meaningful this still builds a small ribbon
/// mesh by hand -- widening each segment toward the current editor camera -- and hands it
/// to AddMesh() instead. The material disables back-face culling since the ribbon's
/// winding isn't guaranteed to face the camera from every angle.
///
/// Reads each NavNode's GlobalPosition rather than its local Position, since NavNodes are
/// referenced via the Nodes array and aren't required to be children of this NavPath.
///
/// The index labels have no font/text-rendering API available on EditorNode3DGizmo (it only
/// offers AddMesh/AddLines/AddUnscaledBillboard, and the latter draws a single billboard at
/// the gizmo's own origin, not at an arbitrary position) so each label is a small textured,
/// camera-facing quad built by hand, with the digits rasterized from a hardcoded 3x5 bitmap
/// font onto an ImageTexture. Cheap enough to rebuild every _Redraw given patrol paths only
/// have a handful of waypoints.
/// </summary>
[Tool]
public partial class NavPathGizmoPlugin : EditorNode3DGizmoPlugin
{
	private const float LineHeight = 0.05f;
	private const float LabelVerticalOffset = 0.4f;
	private const float LabelWorldHeight = 0.4f;
	private const int GlyphPixelWidth = 3;
	private const int GlyphPixelHeight = 5;
	private const int GlyphPixelScale = 4;

	// Each row is a 3-bit mask (MSB = leftmost pixel) for a blocky 3x5 digit glyph.
	private static readonly byte[][] DigitGlyphs =
	{
		new byte[] { 0b111, 0b101, 0b101, 0b101, 0b111 }, // 0
		new byte[] { 0b010, 0b110, 0b010, 0b010, 0b111 }, // 1
		new byte[] { 0b111, 0b001, 0b111, 0b100, 0b111 }, // 2
		new byte[] { 0b111, 0b001, 0b111, 0b001, 0b111 }, // 3
		new byte[] { 0b101, 0b101, 0b111, 0b001, 0b001 }, // 4
		new byte[] { 0b111, 0b100, 0b111, 0b001, 0b111 }, // 5
		new byte[] { 0b111, 0b100, 0b111, 0b101, 0b111 }, // 6
		new byte[] { 0b111, 0b001, 0b001, 0b001, 0b001 }, // 7
		new byte[] { 0b111, 0b101, 0b111, 0b101, 0b111 }, // 8
		new byte[] { 0b111, 0b101, 0b111, 0b001, 0b111 }, // 9
	};

	private readonly StandardMaterial3D _material = new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};

	public override string _GetGizmoName() => nameof(NavPath);

	public override bool _HasGizmo(Node3D forNode) => forNode is NavPath;

	public override void _Redraw(EditorNode3DGizmo gizmo)
	{
		gizmo.Clear();

		if (gizmo.GetNode3D() is not NavPath path)
		{
			return;
		}

		// A silent exception here (e.g. from a stale reload) previously meant the gizmo
		// just looked empty with nothing in the Output panel to explain why -- surface it
		// instead of letting Godot's default virtual-call error handling be the only trace.
		try
		{
			RedrawPath(gizmo, path);
		}
		catch (System.Exception exception)
		{
			GD.PushError($"{nameof(NavPathGizmoPlugin)}._Redraw failed for '{path.Name}': {exception}");
		}
	}

	private void RedrawPath(EditorNode3DGizmo gizmo, NavPath path)
	{
		Camera3D camera = EditorInterface.Singleton?.GetEditorViewport3D(0)?.GetCamera3D();

		var points = new List<Vector3>();
		var navNodes = path.Nodes;
		for (int i = 0; i < navNodes.Count; i++)
		{
			NavNode navNode = navNodes[i];
			if (navNode == null)
			{
				continue;
			}

			Vector3 localPosition = path.ToLocal(navNode.GlobalPosition);
			points.Add(localPosition + Vector3.Up * LineHeight);

			Vector3 labelCenter = localPosition + Vector3.Up * (LineHeight + LabelVerticalOffset);
			gizmo.AddMesh(BuildLabel(path, labelCenter, i, path.LineColor, camera), BuildLabelMaterial(i, path.LineColor));
		}

		if (points.Count < 2)
		{
			return;
		}

		if (path.Looping)
		{
			points.Add(points[0]);
		}

		_material.AlbedoColor = path.LineColor;

		gizmo.AddMesh(BuildRibbon(path, points, path.LineThickness, camera), _material);
	}

	private static StandardMaterial3D BuildLabelMaterial(int index, Color color)
	{
		var image = BuildNumberImage(index, color);
		return new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			AlbedoTexture = ImageTexture.CreateFromImage(image),
		};
	}

	private static Image BuildNumberImage(int number, Color color)
	{
		string digits = number.ToString(CultureInfo.InvariantCulture);
		int glyphWidth = GlyphPixelWidth * GlyphPixelScale;
		int glyphHeight = GlyphPixelHeight * GlyphPixelScale;
		int gap = GlyphPixelScale;
		int width = digits.Length * glyphWidth + (digits.Length - 1) * gap;

		var image = Image.CreateEmpty(width, glyphHeight, false, Image.Format.Rgba8);

		for (int i = 0; i < digits.Length; i++)
		{
			byte[] glyph = DigitGlyphs[digits[i] - '0'];
			int originX = i * (glyphWidth + gap);

			for (int row = 0; row < GlyphPixelHeight; row++)
			{
				byte rowBits = glyph[row];
				for (int col = 0; col < GlyphPixelWidth; col++)
				{
					if ((rowBits & (1 << (GlyphPixelWidth - 1 - col))) == 0)
					{
						continue;
					}

					int blockX = originX + col * GlyphPixelScale;
					int blockY = row * GlyphPixelScale;
					for (int py = 0; py < GlyphPixelScale; py++)
					{
						for (int px = 0; px < GlyphPixelScale; px++)
						{
							image.SetPixel(blockX + px, blockY + py, color);
						}
					}
				}
			}
		}

		return image;
	}

	// Builds a small textured quad that always faces the editor camera, using the camera's
	// own right/up basis vectors directly rather than the ribbon's cross-product trick,
	// since a label has no travel direction to derive a perpendicular from.
	private static ImmediateMesh BuildLabel(Node3D node, Vector3 localCenter, int number, Color color, Camera3D camera)
	{
		string digits = number.ToString(CultureInfo.InvariantCulture);
		float aspect = (digits.Length * (GlyphPixelWidth * GlyphPixelScale) + (digits.Length - 1) * GlyphPixelScale)
			/ (float)(GlyphPixelHeight * GlyphPixelScale);
		float height = LabelWorldHeight;
		float width = height * aspect;

		Vector3 right, up;
		if (camera != null)
		{
			Basis basis = camera.GlobalTransform.Basis;
			right = basis.X;
			up = basis.Y;
		}
		else
		{
			right = Vector3.Right;
			up = Vector3.Up;
		}

		Vector3 globalCenter = node.ToGlobal(localCenter);
		Vector3 halfRight = right.Normalized() * (width * 0.5f);
		Vector3 halfUp = up.Normalized() * (height * 0.5f);

		Vector3 topLeft = node.ToLocal(globalCenter - halfRight + halfUp);
		Vector3 topRight = node.ToLocal(globalCenter + halfRight + halfUp);
		Vector3 bottomLeft = node.ToLocal(globalCenter - halfRight - halfUp);
		Vector3 bottomRight = node.ToLocal(globalCenter + halfRight - halfUp);

		var mesh = new ImmediateMesh();
		mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);

		mesh.SurfaceSetUV(new Vector2(0, 1));
		mesh.SurfaceAddVertex(bottomLeft);
		mesh.SurfaceSetUV(new Vector2(0, 0));
		mesh.SurfaceAddVertex(topLeft);
		mesh.SurfaceSetUV(new Vector2(1, 0));
		mesh.SurfaceAddVertex(topRight);

		mesh.SurfaceSetUV(new Vector2(0, 1));
		mesh.SurfaceAddVertex(bottomLeft);
		mesh.SurfaceSetUV(new Vector2(1, 0));
		mesh.SurfaceAddVertex(topRight);
		mesh.SurfaceSetUV(new Vector2(1, 1));
		mesh.SurfaceAddVertex(bottomRight);

		mesh.SurfaceEnd();
		return mesh;
	}

	// Widens each segment toward the current editor camera (rather than a fixed world-up
	// cross product) so the ribbon reads as a line from any viewing angle -- a flat,
	// ground-plane quad goes edge-on and disappears when looking down at the terrain,
	// which is the normal top-down editor view.
	private static ImmediateMesh BuildRibbon(Node3D node, List<Vector3> points, float thickness, Camera3D camera)
	{
		var mesh = new ImmediateMesh();
		float halfWidth = Mathf.Max(thickness, 0.001f) * 0.5f;

		mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i < points.Count - 1; i++)
		{
			Vector3 a = points[i];
			Vector3 b = points[i + 1];
			Vector3 globalA = node.ToGlobal(a);
			Vector3 globalB = node.ToGlobal(b);
			Vector3 direction = globalB - globalA;

			if (direction.LengthSquared() < 0.0001f)
			{
				continue;
			}

			direction = direction.Normalized();

			Vector3 perpendicular = default;
			if (camera != null)
			{
				Vector3 toCamera = camera.GlobalPosition - (globalA + globalB) * 0.5f;
				perpendicular = direction.Cross(toCamera);
			}

			// Camera missing, or the segment happens to point straight at it: fall back to
			// a horizontal perpendicular so the ribbon still renders as something.
			if (perpendicular.LengthSquared() < 0.0001f)
			{
				perpendicular = new Vector3(-direction.Z, 0f, direction.X);
			}

			perpendicular = perpendicular.Normalized() * halfWidth;

			Vector3 a0 = node.ToLocal(globalA - perpendicular);
			Vector3 a1 = node.ToLocal(globalA + perpendicular);
			Vector3 b0 = node.ToLocal(globalB - perpendicular);
			Vector3 b1 = node.ToLocal(globalB + perpendicular);

			mesh.SurfaceAddVertex(a0);
			mesh.SurfaceAddVertex(a1);
			mesh.SurfaceAddVertex(b1);

			mesh.SurfaceAddVertex(a0);
			mesh.SurfaceAddVertex(b1);
			mesh.SurfaceAddVertex(b0);
		}

		mesh.SurfaceEnd();
		return mesh;
	}
}
