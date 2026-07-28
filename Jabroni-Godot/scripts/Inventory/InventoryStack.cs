namespace Jabroni.Inventory;

/// <summary>
/// One occupied inventory slot: which item, and how many of it. Stackable items get one of these
/// with a count above 1; everything else gets one per unit (see
/// <see cref="Jabroni.Data.ItemSchema.StackableType"/>).
/// </summary>
public sealed class InventoryStack
{
    public InventoryStack(string itemId, int count)
    {
        ItemId = itemId;
        Count = count;
    }

    public string ItemId { get; }

    /// <summary>Only <see cref="PlayerInventory"/> may change this -- it owns the merge/split rules.</summary>
    public int Count { get; internal set; }
}
