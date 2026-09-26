using System.Collections.Generic;
using Godot;

namespace Jabroni.Nav;

/// <summary>
/// An in-world patrol route: authored by dragging NavNode references into the NodePaths array
/// below, one by one, in visit order -- they don't need to be children of this node, just
/// NavNode instances that exist somewhere in the scene. Used directly at runtime by AgentAI,
/// no separate builder/converter step. NavPathGizmoPlugin draws a ribbon connecting the
/// Nodes entries in order, and back to the first if Looping (patrol paths always loop by
/// default), so the route is visible in the 3D viewport without running the game.
///
/// Gizmos only redraw automatically when this node's own transform/properties change, not
/// when a referenced NavNode is moved, so _Process polls UpdateGizmos() every editor frame
/// to keep the ribbon live while authoring a path.
///
/// NodePaths is stored as plain NodePaths rather than an exported Array&lt;NavNode&gt;: on this
/// Godot build, the engine's node_paths auto-resolution for an *array* of a custom script
/// type hands back elements typed as their bare native class (e.g. Marker3D) instead of the
/// attached NavNode script, causing an InvalidCastException wherever a resolved element is
/// used as NavNode. Resolving each NodePath manually via GetNodeOrNull&lt;NavNode&gt; -- the same
/// pattern used everywhere else in this codebase for custom-typed node references -- avoids
/// it. A single node_paths-resolved reference to a custom type (e.g. AgentAI.PatrolPath)
/// works fine; it's specifically arrays that are affected.
/// </summary>
[Tool]
public partial class NavPath : Node3D
{
	private Color _lineColor = new(1f, 0.9f, 0.2f);
	private float _lineThickness = 0.08f;
	private bool _showInGame;
	private MeshInstance3D _runtimeRibbon;

	[Export]
	public Godot.Collections.Array<NodePath> NodePaths { get; set; } = new();

	public IReadOnlyList<NavNode> Nodes
	{
		get
		{
			var nodes = new List<NavNode>(NodePaths.Count);
			foreach (var path in NodePaths)
			{
				var node = GetNodeOrNull<NavNode>(path);
				if (node != null)
				{
					nodes.Add(node);
				}
			}

			return nodes;
		}
	}

	[Export]
	public Color LineColor
	{
		get => _lineColor;
		set { _lineColor = value; UpdateGizmos(); }
	}

	[Export(PropertyHint.Range, "0.01,2.0,0.01,or_greater")]
	public float LineThickness
	{
		get => _lineThickness;
		set { _lineThickness = value; UpdateGizmos(); }
	}

	[Export]
	public bool Looping { get; set; } = true;

	/// <summary>
	/// Draws the path in the running game, not just in the editor. The editor gizmo has no
	/// runtime equivalent -- gizmos don't exist outside the editor -- so this builds the same
	/// ribbon as real geometry instead. Off by default: it rebuilds every frame to keep facing
	/// the camera, which is fine while debugging a patrol and waste the rest of the time.
	///
	/// Drawn without depth testing, so a path that has ended up under the terrain is still
	/// visible -- which is usually the thing you turned this on to find out.
	/// </summary>
	[Export]
	public bool ShowInGame
	{
		get => _showInGame;
		set
		{
			_showInGame = value;
			if (!value)
			{
				ClearRuntimeRibbon();
			}
		}
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
		{
			UpdateGizmos();
			return;
		}

		if (_showInGame)
		{
			DrawRuntimeRibbon();
		}
	}

	private void DrawRuntimeRibbon()
	{
		List<Vector3> points = NavPathRibbon.CollectPoints(this);
		if (points.Count < 2)
		{
			ClearRuntimeRibbon();
			return;
		}

		if (_runtimeRibbon == null)
		{
			_runtimeRibbon = new MeshInstance3D
			{
				Name = "RuntimeRibbon",
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				MaterialOverride = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					CullMode = BaseMaterial3D.CullModeEnum.Disabled,
					NoDepthTest = true,
					AlbedoColor = _lineColor,
				},
			};
			AddChild(_runtimeRibbon);
		}

		if (_runtimeRibbon.MaterialOverride is StandardMaterial3D material)
		{
			material.AlbedoColor = _lineColor;
		}

		_runtimeRibbon.Mesh = NavPathRibbon.Build(
			this, points, _lineThickness, GetViewport()?.GetCamera3D());
	}

	private void ClearRuntimeRibbon()
	{
		if (_runtimeRibbon == null)
		{
			return;
		}

		_runtimeRibbon.QueueFree();
		_runtimeRibbon = null;
	}
}
