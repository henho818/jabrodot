namespace Jabroni.Data;

/// <summary>
/// Item data only -- no inventory container/UI exists yet. Name/Desc are corrected to the real
/// localization keys (I.Copper1 / I.Copper1Desc); the source project's Items_Items.txt actually
/// pointed at "Item.Copper1" / "Item.Copper1Desc", which don't match any localization row -- a
/// data bug in the source, not ported here.
/// </summary>
public partial class ItemRepository : TsvRepository
{
    protected override string DataFilePath => "res://data/Item_Item.txt";
}
