using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Jabroni.Data;

/// <summary>
/// One parsed row of a TSV data table, with typed getters by column name. Mutable so the editor
/// tooling can author rows in place; the game only ever reads.
/// </summary>
public sealed class TsvRow
{
    private readonly Dictionary<string, string> _values;

    public TsvRow(Dictionary<string, string> values)
    {
        _values = values;
    }

    /// <summary>The row's key -- its value in the table's first column.</summary>
    public string Id { get; private init; } = "";

    /// <summary>
    /// Builds a row from a split line, tolerating short lines: a file that stops early at its last
    /// non-empty field leaves the remaining columns blank rather than absent, so every column named
    /// in the header is present and writable.
    /// </summary>
    public static TsvRow FromFields(IReadOnlyList<string> columns, IReadOnlyList<string> fields)
    {
        var values = new Dictionary<string, string>();
        for (int i = 0; i < columns.Count; i++)
        {
            values[columns[i]] = i < fields.Count ? fields[i] : "";
        }

        return new TsvRow(values) { Id = fields.Count > 0 ? fields[0] : "" };
    }

    public string GetString(string column, string fallback = "")
    {
        return _values.TryGetValue(column, out var value) ? value : fallback;
    }

    public void SetString(string column, string value)
    {
        _values[column] = value ?? "";
    }

    public int GetInt(string column, int fallback = 0)
    {
        return _values.TryGetValue(column, out var value) &&
               int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public float GetFloat(string column, float fallback = 0f)
    {
        return _values.TryGetValue(column, out var value) &&
               float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public bool GetBool(string column, bool fallback = false)
    {
        return _values.TryGetValue(column, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    public Color GetColor(string column, Color fallback = default)
    {
        return _values.TryGetValue(column, out var value) && !string.IsNullOrEmpty(value) ? new Color(value) : fallback;
    }
}
