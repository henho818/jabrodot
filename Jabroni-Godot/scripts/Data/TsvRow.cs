using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Jabroni.Data;

/// <summary>One parsed row of a TSV data table, with typed getters by column name.</summary>
public sealed class TsvRow
{
    private readonly Dictionary<string, string> _values;

    public TsvRow(Dictionary<string, string> values)
    {
        _values = values;
    }

    public string GetString(string column, string fallback = "")
    {
        return _values.TryGetValue(column, out var value) ? value : fallback;
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
