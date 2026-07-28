using System.Linq;
using Godot;
using Jabroni.Data;
using Jabroni.Localization;

namespace Jabroni.Core;

public partial class GameBootstrap : Node
{
    public override void _Ready()
    {
        InputActions.Register();
        LocalizationBootstrap.Load();
        ValidateDialogueTables();
    }

    // The Dialogue bottom panel is the place to actually work through these, but a broken Next or
    // a missing localization key otherwise only shows up when someone walks into that specific
    // conversation -- so pressing Play reports them too. Silent when the tables are clean, and
    // skipped entirely in release builds.
    private static void ValidateDialogueTables()
    {
        if (!OS.IsDebugBuild())
        {
            return;
        }

        var diagnostics = DialogGraphValidator.Validate(DialogGraph.Load());
        if (diagnostics.Count == 0)
        {
            return;
        }

        int errors = diagnostics.Count(d => d.Severity == DialogDiagnosticSeverity.Error);
        GD.Print($"[Dialogue] {errors} error(s), {diagnostics.Count - errors} warning(s) "
                 + "-- see the Dialogue panel:");

        foreach (var diagnostic in diagnostics)
        {
            GD.Print($"  [{diagnostic.Severity}] {diagnostic.Subject}: {diagnostic.Message}");
        }
    }
}
