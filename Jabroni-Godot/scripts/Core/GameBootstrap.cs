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
