using System.Collections.Generic;
using Godot;

namespace Jabroni.Data;

/// <summary>Base autoload for a TSV-backed data table, exposing rows by id (the first column).</summary>
public abstract partial class TsvRepository : Node
{
    private Dictionary<string, TsvRow> _rows = new();

    protected abstract string DataFilePath { get; }

    public override void _Ready()
    {
        _rows = TsvTable.Load(DataFilePath);
    }

    public TsvRow Get(string id)
    {
        return _rows.TryGetValue(id, out var row) ? row : null;
    }

    public bool Has(string id) => _rows.ContainsKey(id);

    public IReadOnlyCollection<string> Ids => _rows.Keys;
}
