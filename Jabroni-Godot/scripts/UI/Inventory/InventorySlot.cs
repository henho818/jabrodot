using Godot;
using Jabroni.Data;

namespace Jabroni.UI.Inventory;

/// <summary>
/// One cell of the inventory grid: the item's icon, its stack count, and a selected/unselected
/// frame. A Button rather than a Panel with a click handler, so hover, press and keyboard focus
/// come from the engine instead of being re-implemented per cell.
/// <para>
/// The frames are built here as StyleBoxFlats rather than authored in the scene, matching
/// SubDialogLine -- the grid's whole look is a handful of colours, and keeping them in code means
/// the selected and unselected states can't drift apart in a .tscn nobody opens.
/// </para>
/// </summary>
public partial class InventorySlot : Button
{
    /// <summary>Cell edge length. The grid sizes its columns from this.</summary>
    public const int SlotSize = 76;

    private static readonly Color SlotColor = new(0.13f, 0.14f, 0.17f);
    private static readonly Color SlotBorderColor = new(0.28f, 0.30f, 0.35f);
    private static readonly Color SelectedBorderColor = new(0.93f, 0.78f, 0.35f);

    private TextureRect _icon;
    private Label _count;

    /// <summary>Which item this cell is showing. Empty until <see cref="Setup"/> runs.</summary>
    public string ItemId { get; private set; } = "";

    public override void _Ready()
    {
        _icon = GetNode<TextureRect>("Icon");
        _count = GetNode<Label>("Count");

        CustomMinimumSize = new Vector2(SlotSize, SlotSize);
        SetSelected(false);
    }

    /// <summary>
    /// Points the cell at an item row. A missing or unreadable icon leaves the cell empty and
    /// warns rather than failing the whole grid build -- one bad Icon value shouldn't take the
    /// other eleven items off screen.
    /// </summary>
    public void Setup(string itemId, TsvRow row, int count)
    {
        ItemId = itemId;
        _icon.Texture = LoadIcon(itemId, row);

        // A "1" on every unstackable item is noise -- the count is only worth saying when there
        // is more than one of something.
        _count.Visible = count > 1;
        _count.Text = count.ToString();

        TooltipText = Tr(row.GetString(ItemSchema.NameColumn));
    }

    /// <summary>Swaps the cell's frame between the resting and selected looks.</summary>
    public void SetSelected(bool selected)
    {
        var styleBox = new StyleBoxFlat
        {
            BgColor = SlotColor,
            BorderColor = selected ? SelectedBorderColor : SlotBorderColor,
            BorderWidthLeft = selected ? 3 : 1,
            BorderWidthTop = selected ? 3 : 1,
            BorderWidthRight = selected ? 3 : 1,
            BorderWidthBottom = selected ? 3 : 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
        };

        // Hover and pressed get the same box: the frame already says which cell is selected, and
        // a second highlight competing with it just makes the grid look busy.
        AddThemeStyleboxOverride("normal", styleBox);
        AddThemeStyleboxOverride("hover", styleBox);
        AddThemeStyleboxOverride("pressed", styleBox);
        AddThemeStyleboxOverride("focus", styleBox);
    }

    private static Texture2D LoadIcon(string itemId, TsvRow row)
    {
        string icon = row.GetString(ItemSchema.IconColumn);
        string path = ItemSchema.IconPath(icon);

        if (path == null)
        {
            GD.PushWarning($"InventorySlot: item '{itemId}' has no Icon.");
            return null;
        }

        // Checked rather than loaded blind, so a typo'd Icon is a warning naming the item instead
        // of a raw resource-loader error naming only the path.
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"InventorySlot: item '{itemId}' icon not found at '{path}'.");
            return null;
        }

        return GD.Load<Texture2D>(path);
    }
}
