namespace Jabroni.Data;

/// <summary>
/// The item table: what every item id means, independent of who is holding it (that's
/// <see cref="Jabroni.Inventory.PlayerInventory"/>). Column names live in <see cref="ItemSchema"/>.
/// <para>
/// Name/Desc are the real localization keys (I.Copper1 / I.Copper1Desc); the source project's
/// Items_Items.txt actually pointed at "Item.Copper1" / "Item.Copper1Desc", which don't match any
/// localization row -- a data bug in the source, not ported here.
/// </para>
/// </summary>
public partial class ItemRepository : TsvRepository
{
    protected override string DataFilePath => DataPaths.Item;
}
