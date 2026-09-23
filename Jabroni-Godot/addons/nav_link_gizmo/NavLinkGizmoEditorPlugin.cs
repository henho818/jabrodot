using Godot;
using Jabroni.Nav;

namespace Jabroni.Editor;

/// <summary>
/// Registers NavLinkGizmoPlugin so NavigationLink3D nodes get their direction drawn as a
/// blue-to-red gradient, and declares the editor setting controlling how wide that line is
/// drawn. The drawing itself lives in scripts/Nav/NavLinkGizmoPlugin.cs.
///
/// The width is an editor setting rather than an [Export] because NavigationLink3D is a
/// built-in class and can't be given one without subclassing it -- which would mean the gizmo
/// only worked for links created as the subclass. Declaring it here instead means it applies
/// to every link, and AddPropertyInfo is what turns the bare float into a slider with bounds
/// rather than a free-text field.
/// </summary>
[Tool]
public partial class NavLinkGizmoEditorPlugin : EditorPlugin
{
	private NavLinkGizmoPlugin _gizmoPlugin;

	public override void _EnterTree()
	{
		DeclareLineWidthSetting();

		_gizmoPlugin = new NavLinkGizmoPlugin();
		AddNode3DGizmoPlugin(_gizmoPlugin);
	}

	public override void _ExitTree()
	{
		RemoveNode3DGizmoPlugin(_gizmoPlugin);
		_gizmoPlugin = null;
	}

	private static void DeclareLineWidthSetting()
	{
		EditorSettings settings = EditorInterface.Singleton?.GetEditorSettings();
		if (settings == null)
		{
			return;
		}

		// Only seed the value when it's absent, so a width the user has already dialled in
		// survives the plugin being toggled off and on.
		if (!settings.HasSetting(NavLinkGizmoPlugin.LineWidthSetting))
		{
			settings.SetSetting(NavLinkGizmoPlugin.LineWidthSetting, NavLinkGizmoPlugin.DefaultLineWidth);
		}

		// Drives the revert arrow next to the setting.
		settings.SetInitialValue(NavLinkGizmoPlugin.LineWidthSetting, NavLinkGizmoPlugin.DefaultLineWidth, false);

		settings.AddPropertyInfo(new Godot.Collections.Dictionary
		{
			{ "name", NavLinkGizmoPlugin.LineWidthSetting },
			{ "type", (int)Variant.Type.Float },
			{ "hint", (int)PropertyHint.Range },
			{ "hint_string", "0.01,0.5,0.005" },
		});
	}
}
