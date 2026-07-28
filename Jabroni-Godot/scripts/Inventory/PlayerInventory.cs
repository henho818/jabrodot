using System.Collections.Generic;
using Godot;
using Jabroni.Data;

namespace Jabroni.Inventory;

/// <summary>
/// What the avatar is carrying: an ordered list of stacks, plus the add/remove rules that decide
/// whether an item merges into an existing stack or takes a slot of its own. An autoload rather
/// than a node on the avatar, because the inventory UI lives on a CanvasLayer that knows nothing
/// about the 3D scene, and the same contents should survive a scene change.
/// <para>
/// Order is insertion order and is never re-sorted: the grid draws the stacks in exactly this
/// order, so a slot the player has learned the position of doesn't move under them when an
/// unrelated item is picked up.
/// </para>
/// </summary>
public partial class PlayerInventory : Node
{
    /// <summary>Raised whenever the stacks change -- what the grid rebuilds itself from.</summary>
    [Signal]
    public delegate void ChangedEventHandler();

    /// <summary>
    /// Placeholder starting contents. Nothing awards items yet: Dialog_SubDialog.txt already has
    /// an ItemAward column (see <see cref="DialogSchema.ItemAwardColumn"/>) but DialogBox doesn't
    /// read it, so until it does this is what puts something in the grid to look at. Deliberately
    /// covers both Type values and a mix of counts, so the stacking rules are visible on sight.
    /// </summary>
    private static readonly (string ItemId, int Count)[] StartingContents =
    {
        ("copper1", 47),
        ("silver1", 3),
        ("newspaper", 1),
        ("matchbook", 2),
        ("coffee", 1),
        ("breadroll", 2),
        ("bandage", 4),
        ("umbrella", 1),
        ("roomkey", 1),
        ("letter", 1),
        ("trainticket", 1),
        ("pocketwatch", 1),
    };

    private readonly List<InventoryStack> _stacks = new();

    /// <summary>The occupied slots, in the order the grid should show them.</summary>
    public IReadOnlyList<InventoryStack> Stacks => _stacks;

    public override void _Ready()
    {
        // Relies on ItemRepository being listed ahead of this autoload in project.godot -- Add()
        // reads the item table to decide whether a row stacks, and autoloads run _Ready in the
        // order they are registered.
        foreach (var (itemId, count) in StartingContents)
        {
            Add(itemId, count);
        }
    }

    /// <summary>The item table row backing an id, or null if the table doesn't have it.</summary>
    public TsvRow ItemRow(string itemId)
    {
        return string.IsNullOrEmpty(itemId)
            ? null
            : GetNode<ItemRepository>("/root/ItemRepository").Get(itemId);
    }

    /// <summary>
    /// Adds items, merging into an existing stack when the item's Type says it stacks. An id the
    /// item table doesn't know is a data bug rather than something to invent a slot for, so it
    /// warns and adds nothing.
    /// </summary>
    public void Add(string itemId, int count = 1)
    {
        if (count <= 0)
        {
            return;
        }

        var row = ItemRow(itemId);
        if (row == null)
        {
            GD.PushWarning($"PlayerInventory: unknown item id '{itemId}'");
            return;
        }

        if (IsStackable(row))
        {
            var existing = FindStack(itemId);
            if (existing != null)
            {
                existing.Count += count;
            }
            else
            {
                _stacks.Add(new InventoryStack(itemId, count));
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                _stacks.Add(new InventoryStack(itemId, 1));
            }
        }

        EmitSignal(SignalName.Changed);
    }

    /// <summary>
    /// Removes items, draining stacks oldest-first and dropping any that empty. Returns false and
    /// changes nothing if there aren't enough -- a partial removal would leave a caller that was
    /// spending currency having taken the money without delivering.
    /// </summary>
    public bool Remove(string itemId, int count = 1)
    {
        if (count <= 0)
        {
            return true;
        }

        if (CountOf(itemId) < count)
        {
            return false;
        }

        int remaining = count;
        for (int i = 0; i < _stacks.Count && remaining > 0; i++)
        {
            if (_stacks[i].ItemId != itemId)
            {
                continue;
            }

            int taken = Mathf.Min(_stacks[i].Count, remaining);
            _stacks[i].Count -= taken;
            remaining -= taken;

            if (_stacks[i].Count == 0)
            {
                _stacks.RemoveAt(i);
                i--;
            }
        }

        EmitSignal(SignalName.Changed);
        return true;
    }

    /// <summary>How many of an item are carried, across every stack of it.</summary>
    public int CountOf(string itemId)
    {
        int total = 0;
        foreach (var stack in _stacks)
        {
            if (stack.ItemId == itemId)
            {
                total += stack.Count;
            }
        }

        return total;
    }

    public bool Has(string itemId, int count = 1) => CountOf(itemId) >= count;

    private static bool IsStackable(TsvRow row)
    {
        return row.GetString(ItemSchema.TypeColumn) == ItemSchema.StackableType;
    }

    private InventoryStack FindStack(string itemId)
    {
        foreach (var stack in _stacks)
        {
            if (stack.ItemId == itemId)
            {
                return stack;
            }
        }

        return null;
    }
}
