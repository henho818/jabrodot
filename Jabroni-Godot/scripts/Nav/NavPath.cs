using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Jabroni.Nav;

/// <summary>
/// An in-world patrol route: authored visually as Marker3D-like NavNode children (drag them
/// in the 3D viewport, reorder in the Scene tree), and used directly at runtime by AgentAI --
/// no separate builder/converter step. NavPathGizmoPlugin draws a ribbon connecting the
/// NavNode children in order, and back to the first if Looping (patrol paths always loop by
/// default), so the route is visible in the 3D viewport without running the game.
///
/// Gizmos only redraw automatically when this node's own transform/properties change, not
/// when a child NavNode is dragged, so _Process polls UpdateGizmos() every editor frame to
/// keep the ribbon live while authoring a path.
/// </summary>
[Tool]
public partial class NavPath : Node3D
{
	private Color _lineColor = new(1f, 0.9f, 0.2f);
	private float _lineThickness = 0.08f;

	private List<NavNode> _nodes;

	public IReadOnlyList<NavNode> Nodes => _nodes ??= GetChildren().OfType<NavNode>().ToList();

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

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
		{
			UpdateGizmos();
		}
	}
}
