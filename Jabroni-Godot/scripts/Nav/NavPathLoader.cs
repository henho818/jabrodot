using System.Linq;
using Godot;
using Jabroni.Data;

namespace Jabroni.Nav;

/// <summary>
/// Loads a patrol route from a TSV file (columns Index/X/Y/Z/StayDuration) via the same
/// data pipeline as the rest of the game's content, rather than a Godot Resource -- this
/// Godot build does not apply C# exported-property values from .tres/.tscn files (see
/// ClickToMove.cs's comment for the full finding), so authoring patrol routes as
/// Resources would silently produce empty paths. Revisit if/when that's fixed upstream.
/// </summary>
public static class NavPathLoader
{
    public static NavPath Load(string resourcePath, bool looping = true)
    {
        var rows = TsvTable.Load(resourcePath);
        var nodes = rows.Values
            .OrderBy(row => row.GetInt("Index"))
            .Select(row => new NavNode(
                new Vector3(row.GetFloat("X"), row.GetFloat("Y"), row.GetFloat("Z")),
                row.GetFloat("StayDuration")))
            .ToList();

        return new NavPath(nodes, looping);
    }
}
