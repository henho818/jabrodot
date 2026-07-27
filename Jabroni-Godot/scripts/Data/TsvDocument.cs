using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace Jabroni.Data;

/// <summary>
/// An editable, order-preserving view of a TSV data file: the header columns in their original
/// order, the rows in their original order, and a writer that renders them back out.
/// <para>
/// <see cref="TsvTable"/> stays the read path for the game (it just wants id-to-row lookup).
/// This is the authoring path -- the editor tooling has to write files back without churning
/// them, so column order, row order and the existing habit of trimming trailing empty fields
/// are all preserved. Round-tripping an untouched file reproduces it byte for byte.
/// </para>
/// </summary>
public sealed class TsvDocument
{
    private readonly List<TsvRow> _rows = new();

    private TsvDocument(string resourcePath, IReadOnlyList<string> columns)
    {
        ResourcePath = resourcePath;
        Columns = columns;
    }

    public string ResourcePath { get; }

    /// <summary>Header columns, in file order. The first is the id column.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Rows in file order.</summary>
    public IReadOnlyList<TsvRow> Rows => _rows;

    /// <summary>True once a row has been added, removed or edited through this document.</summary>
    public bool IsDirty { get; private set; }

    public string IdColumn => Columns.Count > 0 ? Columns[0] : "";

    public static TsvDocument Load(string resourcePath)
    {
        using var file = FileAccess.Open(resourcePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"TsvDocument: failed to open '{resourcePath}' ({FileAccess.GetOpenError()})");
            return new TsvDocument(resourcePath, Array.Empty<string>());
        }

        string headerLine = file.GetLine();
        if (string.IsNullOrEmpty(headerLine))
        {
            return new TsvDocument(resourcePath, Array.Empty<string>());
        }

        var document = new TsvDocument(resourcePath, headerLine.Split('\t'));

        while (!file.EofReached())
        {
            string line = file.GetLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            document._rows.Add(TsvRow.FromFields(document.Columns, line.Split('\t')));
        }

        return document;
    }

    /// <summary>First row with this id, or null. Mirrors the read path, which also takes the first.</summary>
    public TsvRow Find(string id)
    {
        return _rows.FirstOrDefault(row => row.Id == id);
    }

    public bool Has(string id) => Find(id) != null;

    /// <summary>Appends a row with every column blank but the id. Fails if the id is already taken.</summary>
    public TsvRow AddRow(string id)
    {
        if (string.IsNullOrEmpty(id) || Has(id))
        {
            return null;
        }

        var row = TsvRow.FromFields(Columns, new[] { id });
        _rows.Add(row);
        IsDirty = true;
        return row;
    }

    public bool RemoveRow(string id)
    {
        int removed = _rows.RemoveAll(row => row.Id == id);
        IsDirty |= removed > 0;
        return removed > 0;
    }

    /// <summary>Sets a cell and marks the document dirty. No-op (but still valid) if unchanged.</summary>
    public bool SetValue(string id, string column, string value)
    {
        var row = Find(id);
        if (row == null || !Columns.Contains(column))
        {
            return false;
        }

        if (row.GetString(column) == value)
        {
            return true;
        }

        row.SetString(column, value);
        IsDirty = true;
        return true;
    }

    /// <summary>Renders the whole file, including its trailing newline.</summary>
    public string Render()
    {
        var builder = new StringBuilder();
        builder.Append(string.Join('\t', Columns)).Append('\n');

        foreach (var row in _rows)
        {
            builder.Append(RenderRow(row)).Append('\n');
        }

        return builder.ToString();
    }

    public Error Save()
    {
        using var file = FileAccess.Open(ResourcePath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            var error = FileAccess.GetOpenError();
            GD.PushError($"TsvDocument: failed to write '{ResourcePath}' ({error})");
            return error == Error.Ok ? Error.Failed : error;
        }

        file.StoreString(Render());
        IsDirty = false;
        return Error.Ok;
    }

    // The hand-authored files stop each line at its last non-empty field rather than padding out
    // to the full column count, so writing them back the same way keeps diffs limited to the rows
    // that actually changed.
    private string RenderRow(TsvRow row)
    {
        var fields = Columns.Select(column => row.GetString(column)).ToList();

        int last = fields.FindLastIndex(field => !string.IsNullOrEmpty(field));
        if (last < 0)
        {
            return "";
        }

        return string.Join('\t', fields.Take(last + 1));
    }

    /// <summary>
    /// True when a value can be stored without corrupting the file. Tabs would invent a column
    /// and newlines would invent a row, so the UI rejects them rather than silently mangling.
    /// </summary>
    public static bool IsStorableValue(string value)
    {
        return value == null || (!value.Contains('\t') && !value.Contains('\n') && !value.Contains('\r'));
    }
}
