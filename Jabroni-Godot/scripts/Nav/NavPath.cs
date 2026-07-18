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
/// LineColor/LineThickness/Looping are exposed as ordinary-looking Inspector properties via
/// _GetPropertyList/_Get/_Set, but are actually stored as node metadata rather than
/// [Export] fields -- see PatrolPathBuilder's original StayDuration handling for why: C#
/// exported-property overrides don't apply from .tscn in this Godot build, but metadata
/// does. The Editor usage flag (no Storage) keeps Godot from also writing a plain property
/// override line into the .tscn, which would just be dead weight since it can't be reloaded
/// on this platform anyway.
///
/// Gizmos only redraw automatically when this node's own transform/properties change, not
/// when a child NavNode is dragged, so _Process polls UpdateGizmos() every editor frame to
/// keep the ribbon live while authoring a path.
/// </summary>
[Tool]
public partial class NavPath : Node3D
{
	private static readonly Color DefaultLineColor = new(1f, 0.9f, 0.2f);
	private const float DefaultLineThickness = 0.08f;
	private const bool DefaultLooping = true;

	private const string LineColorProperty = "LineColor";
	private const string LineThicknessProperty = "LineThickness";
	private const string LoopingProperty = "Looping";

	private List<NavNode> _nodes;

	public IReadOnlyList<NavNode> Nodes => _nodes ??= GetChildren().OfType<NavNode>().ToList();

	public Color LineColor => HasMeta(LineColorProperty) ? GetMeta(LineColorProperty).AsColor() : DefaultLineColor;

	public float LineThickness => HasMeta(LineThicknessProperty) ? GetMeta(LineThicknessProperty).AsSingle() : DefaultLineThickness;

	public bool Looping => HasMeta(LoopingProperty) ? GetMeta(LoopingProperty).AsBool() : DefaultLooping;

	public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList()
	{
		return new Godot.Collections.Array<Godot.Collections.Dictionary>
		{
			new()
			{
				{ "name", LineColorProperty },
				{ "type", (int)Variant.Type.Color },
				{ "usage", (int)PropertyUsageFlags.Editor },
			},
			new()
			{
				{ "name", LineThicknessProperty },
				{ "type", (int)Variant.Type.Float },
				{ "hint", (int)PropertyHint.Range },
				{ "hint_string", "0.01,2.0,0.01,or_greater" },
				{ "usage", (int)PropertyUsageFlags.Editor },
			},
			new()
			{
				{ "name", LoopingProperty },
				{ "type", (int)Variant.Type.Bool },
				{ "usage", (int)PropertyUsageFlags.Editor },
			},
		};
	}

	public override Variant _Get(StringName property)
	{
		if (property == LineColorProperty)
		{
			return LineColor;
		}

		if (property == LineThicknessProperty)
		{
			return LineThickness;
		}

		if (property == LoopingProperty)
		{
			return Looping;
		}

		return default;
	}

	public override bool _Set(StringName property, Variant value)
	{
		if (property == LineColorProperty)
		{
			SetMeta(LineColorProperty, value);
			UpdateGizmos();
			return true;
		}

		if (property == LineThicknessProperty)
		{
			SetMeta(LineThicknessProperty, value);
			UpdateGizmos();
			return true;
		}

		if (property == LoopingProperty)
		{
			SetMeta(LoopingProperty, value);
			return true;
		}

		return false;
	}

	public override bool _PropertyCanRevert(StringName property)
	{
		return property == LineColorProperty || property == LineThicknessProperty || property == LoopingProperty;
	}

	public override Variant _PropertyGetRevert(StringName property)
	{
		if (property == LineColorProperty)
		{
			return DefaultLineColor;
		}

		if (property == LineThicknessProperty)
		{
			return DefaultLineThickness;
		}

		if (property == LoopingProperty)
		{
			return DefaultLooping;
		}

		return default;
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
		{
			UpdateGizmos();
		}
	}
}
