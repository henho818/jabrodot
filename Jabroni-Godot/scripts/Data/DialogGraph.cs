using System.Collections.Generic;
using System.Linq;

namespace Jabroni.Data;

/// <summary>Where a SubDialog line sends the conversation once it's clicked.</summary>
public enum DialogLinkKind
{
    /// <summary>Empty Next -- clicking does nothing; the line is a statement, not a choice.</summary>
    None,

    /// <summary>Next is &lt;end&gt; -- clicking closes the box.</summary>
    End,

    /// <summary>Next names another Dialog to swap in.</summary>
    Jump,
}

/// <summary>One SubDialog line as it sits in a specific Dialog's slot.</summary>
public sealed class DialogLineNode
{
    public string SubDialogId { get; init; }

    /// <summary>Which SubDialogID&lt;n&gt; column this line came from.</summary>
    public int SlotIndex { get; init; }

    /// <summary>False when the Dialog's slot names a SubDialog id that has no row.</summary>
    public bool Resolved { get; init; }

    public string LocalizationId { get; init; } = "";
    public string StyleId { get; init; } = "";
    public string RawNext { get; init; } = "";
    public DialogLinkKind LinkKind { get; init; }

    /// <summary>Target Dialog id when <see cref="LinkKind"/> is Jump, otherwise empty.</summary>
    public string NextDialogId => LinkKind == DialogLinkKind.Jump ? RawNext : "";

    /// <summary>English text for this line, or null when the localization key is missing.</summary>
    public string PreviewText { get; init; }
}

/// <summary>One Dialog row: a single speaker's box and the lines it shows.</summary>
public sealed class DialogNode
{
    public string DialogId { get; init; }
    public string AvatarSheet { get; init; } = "";
    public int AvatarIndex { get; init; } = -1;
    public IReadOnlyList<DialogLineNode> Lines { get; init; } = new List<DialogLineNode>();

    /// <summary>True when no AvatarSheet is set -- the narrator case, where the portrait is hidden.</summary>
    public bool HasPortrait => !string.IsNullOrEmpty(AvatarSheet);
}

/// <summary>An agent config that starts a conversation, i.e. a root of the graph.</summary>
public sealed class DialogEntryPoint
{
    public string ConfigId { get; init; }
    public string AgentName { get; init; } = "";
    public string DialogId { get; init; }
}

/// <summary>
/// A read-only view of the whole dialogue tree, assembled straight from the TSV files rather
/// than from the repository autoloads -- the editor tooling runs with no game running, so it
/// can't reach /root/DialogRepository. Nothing here mutates the data; it just resolves the
/// id-to-id references (Dialog slot -> SubDialog -> Next -> Dialog) into something walkable,
/// and records what didn't resolve so <see cref="DialogGraphValidator"/> can report it.
/// </summary>
public sealed class DialogGraph
{
    public IReadOnlyDictionary<string, DialogNode> Dialogs { get; private init; }
    public IReadOnlyList<DialogEntryPoint> EntryPoints { get; private init; }

    /// <summary>Style ids defined in Dialog_SubDialogStyle.txt.</summary>
    public IReadOnlySet<string> StyleIds { get; private init; }

    /// <summary>Every SubDialog id that has a row, whether or not a Dialog uses it.</summary>
    public IReadOnlySet<string> SubDialogIds { get; private init; }

    /// <summary>Localization keys mapped to their per-locale text, for missing/blank checks.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Localization { get; private init; }

    /// <summary>Reads the tables from disk. Used by the game and by a fresh editor session.</summary>
    public static DialogGraph Load()
    {
        return Build(
            TsvDocument.Load(DataPaths.Dialog),
            TsvDocument.Load(DataPaths.SubDialog),
            TsvDocument.Load(DataPaths.SubDialogStyle),
            TsvDocument.Load(DataPaths.AgentConfig),
            TsvDocument.Load(DataPaths.Localization));
    }

    /// <summary>
    /// Derives the graph from already-loaded documents. The editor rebuilds after every edit, so
    /// it needs to see unsaved changes -- which means reading the in-memory rows, not the files.
    /// </summary>
    public static DialogGraph Build(
        TsvDocument dialogDocument,
        TsvDocument subDialogDocument,
        TsvDocument styleDocument,
        TsvDocument agentDocument,
        TsvDocument localizationDocument)
    {
        var subDialogRows = new Dictionary<string, TsvRow>();
        foreach (var row in subDialogDocument.Rows)
        {
            subDialogRows.TryAdd(row.Id, row);
        }

        var localization = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var row in localizationDocument.Rows)
        {
            localization.TryAdd(row.Id, DialogSchema.Locales.ToDictionary(
                locale => locale,
                locale => row.GetString(locale)));
        }

        var dialogs = new Dictionary<string, DialogNode>();
        foreach (var row in dialogDocument.Rows)
        {
            if (!dialogs.ContainsKey(row.Id))
            {
                dialogs[row.Id] = BuildDialogNode(row.Id, row, subDialogRows, localization);
            }
        }

        var entryPoints = agentDocument.Rows
            .Select(row => new DialogEntryPoint
            {
                ConfigId = row.Id,
                AgentName = row.GetString(DialogSchema.AgentNameColumn),
                DialogId = row.GetString(DialogSchema.AgentDialogIdColumn),
            })
            .Where(entry => !string.IsNullOrEmpty(entry.DialogId))
            .OrderBy(entry => entry.ConfigId)
            .ToList();

        return new DialogGraph
        {
            Dialogs = dialogs,
            EntryPoints = entryPoints,
            StyleIds = styleDocument.Rows.Select(row => row.Id).ToHashSet(),
            SubDialogIds = subDialogRows.Keys.ToHashSet(),
            Localization = localization,
        };
    }

    private static DialogNode BuildDialogNode(
        string dialogId,
        TsvRow row,
        IReadOnlyDictionary<string, TsvRow> subDialogRows,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> localization)
    {
        var lines = new List<DialogLineNode>();

        for (int slot = 0; slot < DialogSchema.SubDialogSlotColumns.Length; slot++)
        {
            string subDialogId = row.GetString(DialogSchema.SubDialogSlotColumns[slot]);
            if (string.IsNullOrEmpty(subDialogId))
            {
                continue;
            }

            if (!subDialogRows.TryGetValue(subDialogId, out var subRow))
            {
                lines.Add(new DialogLineNode { SubDialogId = subDialogId, SlotIndex = slot, Resolved = false });
                continue;
            }

            string localizationId = subRow.GetString(DialogSchema.LocalizationIdColumn);
            string rawNext = subRow.GetString(DialogSchema.NextColumn);

            lines.Add(new DialogLineNode
            {
                SubDialogId = subDialogId,
                SlotIndex = slot,
                Resolved = true,
                LocalizationId = localizationId,
                StyleId = subRow.GetString(DialogSchema.StyleColumn),
                RawNext = rawNext,
                LinkKind = ClassifyNext(rawNext),
                PreviewText = localization.TryGetValue(localizationId, out var text)
                    ? text[DialogSchema.PreviewLocale]
                    : null,
            });
        }

        return new DialogNode
        {
            DialogId = dialogId,
            AvatarSheet = row.GetString(DialogSchema.AvatarSheetColumn),
            AvatarIndex = row.GetInt(DialogSchema.AvatarIndexColumn, -1),
            Lines = lines,
        };
    }

    private static DialogLinkKind ClassifyNext(string rawNext)
    {
        if (string.IsNullOrEmpty(rawNext))
        {
            return DialogLinkKind.None;
        }

        return rawNext == DialogSchema.EndCommand ? DialogLinkKind.End : DialogLinkKind.Jump;
    }

    /// <summary>Dialog ids reachable by following Next links out from the agent-config entry points.</summary>
    public HashSet<string> ReachableDialogIds()
    {
        var reached = new HashSet<string>();
        var pending = new Queue<string>();

        foreach (var entry in EntryPoints)
        {
            if (Dialogs.ContainsKey(entry.DialogId) && reached.Add(entry.DialogId))
            {
                pending.Enqueue(entry.DialogId);
            }
        }

        while (pending.Count > 0)
        {
            foreach (var line in Dialogs[pending.Dequeue()].Lines)
            {
                string next = line.NextDialogId;
                if (!string.IsNullOrEmpty(next) && Dialogs.ContainsKey(next) && reached.Add(next))
                {
                    pending.Enqueue(next);
                }
            }
        }

        return reached;
    }

    /// <summary>Every SubDialog id actually placed in a Dialog slot.</summary>
    public HashSet<string> UsedSubDialogIds()
    {
        return Dialogs.Values
            .SelectMany(dialog => dialog.Lines)
            .Select(line => line.SubDialogId)
            .ToHashSet();
    }
}
