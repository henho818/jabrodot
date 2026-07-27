using System.Collections.Generic;
using System.Linq;
using Godot;
using Jabroni.Data;

namespace Jabroni.Editor;

/// <summary>
/// The list of Dialog ids the Inspector offers and validates against, cached so that every
/// redraw of a ChatDialogId field doesn't re-read the table off disk.
/// <para>
/// The cache is keyed on Dialog_Dialog.txt's modification time, so ids created in the Dialogue
/// panel appear here as soon as that panel saves -- but not before. A Dialog that only exists
/// as an unsaved edit will read as invalid until it's written, which matches the fact that the
/// game would not find it either.
/// </para>
/// </summary>
internal static class DialogIdCatalog
{
    private static IReadOnlyList<string> _ids = new List<string>();
    private static ulong _loadedStamp;
    private static bool _loaded;

    /// <summary>Every Dialog id, in table order.</summary>
    public static IReadOnlyList<string> Ids
    {
        get
        {
            Refresh();
            return _ids;
        }
    }

    public static bool Contains(string dialogId)
    {
        Refresh();
        return _ids.Contains(dialogId);
    }

    private static void Refresh()
    {
        ulong stamp = FileAccess.GetModifiedTime(DataPaths.Dialog);
        if (_loaded && stamp == _loadedStamp)
        {
            return;
        }

        _ids = TsvDocument.Load(DataPaths.Dialog).Rows.Select(row => row.Id).ToList();
        _loadedStamp = stamp;
        _loaded = true;
    }
}
