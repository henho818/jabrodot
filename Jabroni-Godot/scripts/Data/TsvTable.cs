using System.Collections.Generic;

namespace Jabroni.Data;

/// <summary>
/// The game's read path for a TSV data file: rows keyed by the first column. Parsing lives in
/// <see cref="TsvDocument"/> so the runtime and the editor tooling can't disagree about how a
/// file is read; this just drops the ordering the authoring side needs and hands back a lookup.
/// </summary>
public static class TsvTable
{
    public static Dictionary<string, TsvRow> Load(string resourcePath)
    {
        var result = new Dictionary<string, TsvRow>();

        // First occurrence wins, matching TsvDocument.Find -- a duplicate id is an authoring
        // mistake the dialogue validator reports rather than something to resolve silently.
        foreach (var row in TsvDocument.Load(resourcePath).Rows)
        {
            result.TryAdd(row.Id, row);
        }

        return result;
    }
}
