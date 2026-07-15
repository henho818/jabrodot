using Godot;

namespace Jabroni.UI.Dialog;

/// <summary>
/// Maps an AvatarIndex (0-15) to its cell rect in art/avatars.png. The source sheet is a
/// manually-sliced 4x4 grid with uneven gutters (wider vertically than horizontally), so cells
/// are looked up individually rather than computed from a uniform cell-size formula -- values
/// ported directly from the source project's sprite-slice metadata (avatars.png.meta), with
/// Unity's bottom-left-origin y flipped to Godot's top-left-origin.
/// </summary>
public static class AvatarSheet
{
    public const string TexturePath = "res://art/avatars.png";

    private static readonly Rect2[] CellRects =
    {
        new(125, 125, 510, 510),
        new(665, 125, 510, 510),
        new(1205, 125, 510, 510),
        new(1745, 125, 510, 510),
        new(125, 795, 510, 510),
        new(665, 795, 510, 510),
        new(1205, 795, 510, 510),
        new(1745, 795, 510, 510),
        new(125, 1465, 510, 510),
        new(665, 1465, 510, 510),
        new(1205, 1465, 510, 510),
        new(1745, 1465, 510, 510),
        new(125, 2135, 510, 510),
        new(665, 2135, 510, 510),
        new(1205, 2135, 510, 510),
        new(1745, 2135, 510, 510),
    };

    public static Rect2? GetCellRect(int index)
    {
        return index >= 0 && index < CellRects.Length ? CellRects[index] : null;
    }
}
