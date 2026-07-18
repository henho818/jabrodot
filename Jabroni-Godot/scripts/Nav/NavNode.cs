using Godot;

namespace Jabroni.Nav;

/// <summary>
/// A single patrol waypoint, authored visually as a child of a NavPath node (drag it in the
/// 3D viewport, reorder in the Scene tree). Position comes from the node itself; StayDuration
/// is exposed as an ordinary-looking Inspector property via _GetPropertyList/_Get/_Set, but
/// is actually stored as node metadata rather than an [Export] field -- see
/// PatrolPathBuilder's original StayDuration handling for why: C# exported-property
/// overrides don't apply from .tscn in this Godot build, but metadata does. The Editor usage
/// flag (no Storage) keeps Godot from also writing a plain property override line into the
/// .tscn, which would just be dead weight since it can't be reloaded on this platform anyway.
/// </summary>
[Tool]
public partial class NavNode : Node3D
{
	private const double DefaultStayDuration = 1.5;
	private const string StayDurationProperty = "StayDuration";

	/// <summary>
	/// Snapped-to-terrain world position, set by NavPathTerrainSnapper. Distinct from the
	/// inherited Position (local, author-time authoring value) since locomotion needs the
	/// resolved world target.
	/// </summary>
	public Vector3 WorldPosition => GlobalPosition;

	public double StayDuration => HasMeta(StayDurationProperty) ? GetMeta(StayDurationProperty).AsDouble() : DefaultStayDuration;

	public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList()
	{
		return new Godot.Collections.Array<Godot.Collections.Dictionary>
		{
			new()
			{
				{ "name", StayDurationProperty },
				{ "type", (int)Variant.Type.Float },
				{ "hint", (int)PropertyHint.Range },
				{ "hint_string", "0,30,0.1,or_greater" },
				{ "usage", (int)PropertyUsageFlags.Editor },
			},
		};
	}

	public override Variant _Get(StringName property)
	{
		if (property == StayDurationProperty)
		{
			return StayDuration;
		}

		return default;
	}

	public override bool _Set(StringName property, Variant value)
	{
		if (property == StayDurationProperty)
		{
			SetMeta(StayDurationProperty, value);
			return true;
		}

		return false;
	}

	public override bool _PropertyCanRevert(StringName property)
	{
		return property == StayDurationProperty;
	}

	public override Variant _PropertyGetRevert(StringName property)
	{
		if (property == StayDurationProperty)
		{
			return DefaultStayDuration;
		}

		return default;
	}
}
