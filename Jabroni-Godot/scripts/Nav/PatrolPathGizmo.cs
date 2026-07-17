using System.Collections.Generic;
using Godot;

namespace Jabroni.Nav;

/// <summary>
/// Editor-only visual: draws a ribbon connecting this node's Marker3D children in order,
/// and back to the first (patrol paths always loop), so the route is visible in the 3D
/// viewport without running the game. Does nothing at runtime.
///
/// Color/thickness are read from this node's metadata (Inspector -> Metadata panel) rather
/// than [Export] -- see PatrolPathBuilder's StayDuration handling for why: C# exported-
/// property overrides don't apply from .tscn in this Godot build, but metadata does.
/// Add a "LineColor" (Color) and/or "LineThickness" (float) entry to override the defaults.
/// </summary>
[Tool]
public partial class PatrolPathGizmo : Node3D
{
	private const float LineHeight = 0.05f;
	private static readonly Color DefaultLineColor = new(1f, 0.9f, 0.2f);
	private const float DefaultLineThickness = 0.08f;

	private const string LinesNodeName = "__PatrolPathLines";

	private MeshInstance3D _lines;
	private StandardMaterial3D _material;

	public override void _Process(double delta)
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		// Fields don't reliably survive the editor's C# hot-reload (the child node does,
		// reconnected by name, but plain fields reset to null without _Ready rerunning) --
		// so re-resolve/recreate here instead of trusting _Ready ran for this instance.
		if (_lines == null || !GodotObject.IsInstanceValid(_lines))
		{
			_lines = GetNodeOrNull<MeshInstance3D>(LinesNodeName);
		}

		if (_lines == null)
		{
			_material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
			_lines = new MeshInstance3D { Name = LinesNodeName, MaterialOverride = _material };
			AddChild(_lines);
		}
		else if (_material == null || !GodotObject.IsInstanceValid(_material))
		{
			_material = _lines.MaterialOverride as StandardMaterial3D
				?? new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
			_lines.MaterialOverride = _material;
		}

		_material.AlbedoColor = HasMeta("LineColor") ? GetMeta("LineColor").AsColor() : DefaultLineColor;
		float thickness = HasMeta("LineThickness") ? GetMeta("LineThickness").AsSingle() : DefaultLineThickness;

		_lines.Mesh = BuildRibbon(thickness);
	}

	private ImmediateMesh BuildRibbon(float thickness)
	{
		var mesh = new ImmediateMesh();
		var points = new List<Vector3>();

		foreach (var child in GetChildren())
		{
			if (child is Marker3D marker)
			{
				points.Add(marker.Position + Vector3.Up * LineHeight);
			}
		}

		if (points.Count < 2)
		{
			return mesh;
		}

		points.Add(points[0]); // close the loop
		float halfWidth = Mathf.Max(thickness, 0.001f) * 0.5f;

		mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i < points.Count - 1; i++)
		{
			Vector3 a = points[i];
			Vector3 b = points[i + 1];
			Vector3 direction = b - a;
			direction.Y = 0f;

			if (direction.LengthSquared() < 0.0001f)
			{
				continue;
			}

			direction = direction.Normalized();
			Vector3 perpendicular = new Vector3(-direction.Z, 0f, direction.X) * halfWidth;

			Vector3 a0 = a - perpendicular;
			Vector3 a1 = a + perpendicular;
			Vector3 b0 = b - perpendicular;
			Vector3 b1 = b + perpendicular;

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
