using Godot;
using Jabroni.Data;
using Jabroni.Inventory;

namespace Jabroni.UI.Inventory;

/// <summary>
/// The grid of item cells. Rebuilds itself from <see cref="PlayerInventory"/> whenever the
/// contents change and owns which cell is selected, announcing it via
/// <see cref="SelectionChangedEventHandler"/> so the panel around it can show the item's details
/// without either half having to reach into the other.
/// </summary>
public partial class InventoryGrid : GridContainer
{
    /// <summary>The newly selected item's id, or an empty string when the selection was cleared.</summary>
    [Signal]
    public delegate void SelectionChangedEventHandler(string itemId);

    private const string SlotScenePath = "res://scenes/UI/InventorySlot.tscn";

    private PackedScene _slotScene;
    private PlayerInventory _inventory;

    /// <summary>
    /// Selection is by slot index, not item id: unstackable items take a slot each, so two room
    /// keys are two cells sharing one id and an id alone couldn't say which was clicked.
    /// </summary>
    private int _selectedIndex = -1;

    /// <summary>How many cells are currently shown -- what the panel checks for an empty bag.</summary>
    public int SlotCount => GetChildCount();

    public override void _Ready()
    {
        _slotScene = GD.Load<PackedScene>(SlotScenePath);
        _inventory = GetNode<PlayerInventory>("/root/PlayerInventory");

        _inventory.Changed += Rebuild;
        Rebuild();
    }

    public override void _ExitTree()
    {
        _inventory.Changed -= Rebuild;
    }

    /// <summary>Drops the selection, e.g. when the panel is closed and reopened.</summary>
    public void ClearSelection()
    {
        Select(-1);
    }

    private void Rebuild()
    {
        // Immediate detach before QueueFree, for the same reason DialogBox.ClearLines does it:
        // a queue-freed child is still counted by the container's layout for the rest of the
        // frame, which here would leave the panel sized for the old contents plus the new.
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        var stacks = _inventory.Stacks;
        for (int i = 0; i < stacks.Count; i++)
        {
            var stack = stacks[i];
            TsvRow row = _inventory.ItemRow(stack.ItemId);
            if (row == null)
            {
                // PlayerInventory.Add already refused unknown ids, so reaching here means the
                // table changed under a live inventory.
                GD.PushWarning($"InventoryGrid: no item row for '{stack.ItemId}', skipping cell.");
                continue;
            }

            var slot = _slotScene.Instantiate<InventorySlot>();
            AddChild(slot);
            slot.Setup(stack.ItemId, row, stack.Count);

            int index = i;
            slot.Pressed += () => Select(index);
        }

        // Whatever was selected belonged to the old layout; anything past the end is gone, and
        // anything still in range may now be a different item, so the safe move is to re-announce
        // what is under the old index rather than assume it survived.
        Select(_selectedIndex < SlotCount ? _selectedIndex : -1);
    }

    private void Select(int index)
    {
        _selectedIndex = index;

        for (int i = 0; i < GetChildCount(); i++)
        {
            GetChild<InventorySlot>(i).SetSelected(i == index);
        }

        string itemId = index >= 0 && index < GetChildCount() ? GetChild<InventorySlot>(index).ItemId : "";
        EmitSignal(SignalName.SelectionChanged, itemId);
    }
}
