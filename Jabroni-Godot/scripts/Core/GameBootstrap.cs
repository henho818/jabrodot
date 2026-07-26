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
        DebugPrintItemSmokeTest();
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

    // Temporary console verification for M11 (ItemRepository data loading, no runtime UI yet
    // to exercise it visually) -- remove once confirmed.
    private void DebugPrintItemSmokeTest()
    {
        var itemRepo = GetNode<ItemRepository>("/root/ItemRepository");
        var row = itemRepo.Get("copper1");
        if (row == null)
        {
            GD.PushWarning("[ItemTest] copper1 not found.");
            return;
        }

        string name = Tr(row.GetString("Name"));
        string desc = Tr(row.GetString("Desc"));
        int value = row.GetInt("Value");
        int sellValue = row.GetInt("SellValue");

        GD.Print($"[ItemTest] copper1: name='{name}' desc='{desc}' value={value} sellValue={sellValue}");
    }
}
