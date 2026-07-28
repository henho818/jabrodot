namespace Jabroni.Data;

/// <summary>
/// Column names and magic values for Item_Item.txt, the same job <see cref="DialogSchema"/> does
/// for the dialogue tables: the inventory model and its UI both read the table through here, so
/// a renamed column is one edit rather than a hunt through string literals.
/// </summary>
public static class ItemSchema
{
    // Item_Item.txt
    public const string ItemIdColumn = "ItemId";

    /// <summary>How the item is held: <see cref="StackableType"/>, or anything else for one slot each.</summary>
    public const string TypeColumn = "Type";

    /// <summary>What kind of thing it is -- currency, tool, key, consumable, document, valuable.</summary>
    public const string SubTypeColumn = "SubType";

    /// <summary>Localization key for the display name, not the name itself (e.g. "I.Copper1").</summary>
    public const string NameColumn = "Name";

    /// <summary>Localization key for the description (e.g. "I.Copper1Desc").</summary>
    public const string DescColumn = "Desc";

    /// <summary>Icon file's base name, resolved to a path by <see cref="IconPath"/>.</summary>
    public const string IconColumn = "Icon";

    /// <summary>3D model for the item lying in the world. Unused -- no item models exist yet.</summary>
    public const string ModelColumn = "Model";

    public const string ValueColumn = "Value";
    public const string SellValueColumn = "SellValue";

    /// <summary>
    /// The Type value marking an item that merges into a single slot with a count on it. Anything
    /// else takes a slot per unit, so two room keys read as two keys rather than "key x2".
    /// </summary>
    public const string StackableType = "stack";

    private const string IconDirectory = "res://art/items/";

    /// <summary>
    /// Icons are SVG rather than raster: they are placeholder art that will be redrawn, and the
    /// project keeps PNG/JPG in Git LFS (see .gitattributes), which is a heavy way to carry a
    /// dozen flat shapes. Godot rasterizes them at import (see the .import files' svg/scale).
    /// </summary>
    private const string IconExtension = ".svg";

    /// <summary>The resource path for an <see cref="IconColumn"/> value, or null when it is blank.</summary>
    public static string IconPath(string icon)
    {
        return string.IsNullOrEmpty(icon) ? null : IconDirectory + icon + IconExtension;
    }
}
