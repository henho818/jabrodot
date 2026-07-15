using System.Collections.Generic;
using Godot;

namespace Jabroni.Data;

/// <summary>
/// Parses a tab-separated data file (header row + data rows) into rows keyed by the
/// first column. This is a simplified, idiomatic-C# stand-in for the source project's
/// TextDataParser/BaseRepository pair -- it skips their enum/reflection header-mapping
/// layer in favor of looking up columns by name directly via TsvRow.
/// </summary>
public static class TsvTable
{
    public static Dictionary<string, TsvRow> Load(string resourcePath)
    {
        var result = new Dictionary<string, TsvRow>();

        using var file = FileAccess.Open(resourcePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"TsvTable: failed to open '{resourcePath}' ({FileAccess.GetOpenError()})");
            return result;
        }

        string headerLine = file.GetLine();
        if (string.IsNullOrEmpty(headerLine))
        {
            return result;
        }

        string[] columns = headerLine.Split('\t');

        while (!file.EofReached())
        {
            string line = file.GetLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = line.Split('\t');
            var values = new Dictionary<string, string>();
            for (int i = 0; i < columns.Length && i < fields.Length; i++)
            {
                values[columns[i]] = fields[i];
            }

            result[fields[0]] = new TsvRow(values);
        }

        return result;
    }
}
