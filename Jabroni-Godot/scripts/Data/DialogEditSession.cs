using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Jabroni.Data;

/// <summary>Outcome of one edit, with a line of text the panel can show the author.</summary>
public readonly struct EditResult
{
    public bool Ok { get; init; }
    public string Message { get; init; }

    public static EditResult Success(string message) => new() { Ok = true, Message = message };
    public static EditResult Failure(string message) => new() { Ok = false, Message = message };
}

/// <summary>A line to create: its SubDialog row, plus the Localization row behind it.</summary>
public sealed class NewLineRequest
{
    public string SubDialogId { get; set; } = "";
    public string LocalizationId { get; set; } = "";
    public string EnglishText { get; set; } = "";
    public string StyleId { get; set; } = "";

    /// <summary>Empty, <c>&lt;end&gt;</c>, or an existing Dialog id.</summary>
    public string Next { get; set; } = "";

    /// <summary>Empty, or an item id the line hands over when it's clicked through.</summary>
    public string ItemAward { get; set; } = "";

    /// <summary>Empty, or an item id the line is gated on. Not read at runtime yet.</summary>
    public string ItemDependency { get; set; } = "";

    public float Pitch { get; set; } = 2f;
}

/// <summary>A Dialog to create, along with the first line it shows.</summary>
public sealed class NewDialogRequest
{
    public string DialogId { get; set; } = "";
    public bool HasPortrait { get; set; } = true;
    public int AvatarIndex { get; set; }
    public NewLineRequest FirstLine { get; set; } = new();
}

/// <summary>
/// The authoring model behind the Dialogue panel: holds the dialogue tables as editable
/// documents, exposes the operations the graph UI needs in terms the data actually has
/// (dialogs, lines, slots, jumps), and re-derives a <see cref="DialogGraph"/> after each one so
/// the view and the validator always reflect unsaved state.
/// <para>
/// Nothing here touches disk until <see cref="Save"/> is called, so any edit -- including the
/// destructive ones -- is undone by <see cref="Reload"/>.
/// </para>
/// </summary>
public sealed class DialogEditSession
{
    private DialogEditSession()
    {
    }

    public TsvDocument DialogDocument { get; private set; }
    public TsvDocument SubDialogDocument { get; private set; }
    public TsvDocument StyleDocument { get; private set; }
    public TsvDocument LocalizationDocument { get; private set; }

    /// <summary>
    /// Item_Item.txt, for offering and checking ItemAward/ItemDependency values. Read-only here
    /// for the same reason styles are: this panel authors dialogue, not the item table.
    /// </summary>
    public TsvDocument ItemDocument { get; private set; }

    /// <summary>
    /// The graph's roots, read from the scenes on Reload. They aren't editable here -- moving a
    /// Dialog onto a different agent is a scene edit, done in the Inspector.
    /// </summary>
    public IReadOnlyList<DialogEntryPoint> EntryPoints { get; private set; }

    /// <summary>Rebuilt after every mutation; never null.</summary>
    public DialogGraph Graph { get; private set; }

    public bool IsDirty => Documents.Any(document => document.IsDirty);

    /// <summary>The tables this session may write. Styles and items are read-only here.</summary>
    private IEnumerable<TsvDocument> Documents
    {
        get
        {
            yield return DialogDocument;
            yield return SubDialogDocument;
            yield return LocalizationDocument;
        }
    }

    public static DialogEditSession Load()
    {
        var session = new DialogEditSession();
        session.Reload();
        return session;
    }

    /// <summary>Re-reads every table from disk, rescans the scenes, and discards unsaved edits.</summary>
    public void Reload()
    {
        DialogDocument = TsvDocument.Load(DataPaths.Dialog);
        SubDialogDocument = TsvDocument.Load(DataPaths.SubDialog);
        StyleDocument = TsvDocument.Load(DataPaths.SubDialogStyle);
        LocalizationDocument = TsvDocument.Load(DataPaths.Localization);
        ItemDocument = TsvDocument.Load(DataPaths.Item);
        EntryPoints = DialogEntryPointScanner.Scan();
        RebuildGraph();
    }

    /// <summary>Writes only the tables that changed. Returns a summary, or the first failure.</summary>
    public EditResult Save()
    {
        var written = new List<string>();

        foreach (var document in Documents.Where(document => document.IsDirty))
        {
            var error = document.Save();
            if (error != Error.Ok)
            {
                return EditResult.Failure($"Failed to write {document.ResourcePath} ({error}).");
            }

            written.Add(document.ResourcePath.Replace("res://data/", ""));
        }

        return written.Count == 0
            ? EditResult.Success("Nothing to save.")
            : EditResult.Success($"Saved {string.Join(", ", written)}.");
    }

    private void RebuildGraph()
    {
        Graph = DialogGraph.Build(
            DialogDocument, SubDialogDocument, StyleDocument, EntryPoints, LocalizationDocument,
            ItemDocument);
    }

    public IReadOnlyList<string> StyleIds => StyleDocument.Rows.Select(row => row.Id).ToList();

    public IReadOnlyList<string> DialogIds => DialogDocument.Rows.Select(row => row.Id).ToList();

    public IReadOnlyList<string> ItemIds => ItemDocument.Rows.Select(row => row.Id).ToList();

    /// <summary>
    /// English name for an item id, for labelling the authoring dropdowns -- the item table stores
    /// a localization key in Name, which on its own reads as "I.Copper1" rather than "Copper Coin".
    /// Falls back to the raw key, then to the id, so a half-authored item still labels itself.
    /// </summary>
    public string ItemDisplayName(string itemId)
    {
        string nameKey = ItemDocument.Find(itemId)?.GetString(ItemSchema.NameColumn) ?? "";
        if (string.IsNullOrEmpty(nameKey))
        {
            return itemId;
        }

        string english = LocalizationDocument.Find(nameKey)?.GetString(DialogSchema.PreviewLocale) ?? "";
        return string.IsNullOrEmpty(english) ? nameKey : english;
    }

    // ---- creation ----------------------------------------------------------------------

    public EditResult CreateDialog(NewDialogRequest request)
    {
        string dialogId = request.DialogId?.Trim() ?? "";

        var idCheck = ValidateNewId(dialogId, DialogDocument, "Dialog");
        if (!idCheck.Ok)
        {
            return idCheck;
        }

        var lineCheck = ValidateNewLine(request.FirstLine);
        if (!lineCheck.Ok)
        {
            return lineCheck;
        }

        var row = DialogDocument.AddRow(dialogId);
        row.SetString(DialogSchema.AvatarSheetColumn, request.HasPortrait ? DialogSchema.DefaultAvatarSheet : "");
        row.SetString(DialogSchema.AvatarIndexColumn, request.HasPortrait ? request.AvatarIndex.ToString() : "");
        row.SetString(DialogSchema.SubDialogSlotColumns[0], request.FirstLine.SubDialogId.Trim());

        string localizationNote = WriteLine(request.FirstLine);
        RebuildGraph();

        return EditResult.Success($"Created {dialogId} with line {request.FirstLine.SubDialogId.Trim()}.{localizationNote}");
    }

    public EditResult AddLine(string dialogId, NewLineRequest request)
    {
        var dialogRow = DialogDocument.Find(dialogId);
        if (dialogRow == null)
        {
            return EditResult.Failure($"'{dialogId}' is not a Dialog.");
        }

        var lineCheck = ValidateNewLine(request);
        if (!lineCheck.Ok)
        {
            return lineCheck;
        }

        string slot = DialogSchema.SubDialogSlotColumns
            .FirstOrDefault(column => string.IsNullOrEmpty(dialogRow.GetString(column)));

        if (slot == null)
        {
            return EditResult.Failure(
                $"{dialogId} already fills all {DialogSchema.SubDialogSlotColumns.Length} SubDialog slots.");
        }

        DialogDocument.SetValue(dialogId, slot, request.SubDialogId.Trim());

        string localizationNote = WriteLine(request);
        RebuildGraph();

        return EditResult.Success($"Added {request.SubDialogId.Trim()} to {dialogId} ({slot}).{localizationNote}");
    }

    /// <summary>Writes the SubDialog row and makes sure a Localization row exists. Returns a note for the caller's message.</summary>
    private string WriteLine(NewLineRequest request)
    {
        string subDialogId = request.SubDialogId.Trim();
        string localizationId = request.LocalizationId.Trim();

        var row = SubDialogDocument.AddRow(subDialogId);
        row.SetString(DialogSchema.LocalizationIdColumn, localizationId);
        row.SetString(DialogSchema.StyleColumn, request.StyleId);
        row.SetString(DialogSchema.NextColumn, request.Next);
        row.SetString(DialogSchema.ItemAwardColumn, request.ItemAward);
        row.SetString(DialogSchema.ItemDependencyColumn, request.ItemDependency);
        row.SetString(DialogSchema.PitchColumn, request.Pitch.ToString("0.###"));

        // An existing key is a deliberate reuse (several lines share LD.OK today), so its text --
        // and any translations already done against it -- is left exactly as it is.
        if (LocalizationDocument.Has(localizationId))
        {
            return $" Reused existing key {localizationId}; its text was left unchanged.";
        }

        var localizationRow = LocalizationDocument.AddRow(localizationId);
        localizationRow.SetString(DialogSchema.PreviewLocale, request.EnglishText);
        return $" Added {localizationId} to Localization.tsv ({DialogSchema.Locales.Length - 1} locales still blank).";
    }

    // ---- linking -----------------------------------------------------------------------

    /// <summary>Points a line at another Dialog, at &lt;end&gt;, or nowhere.</summary>
    public EditResult SetNext(string subDialogId, string next)
    {
        if (SubDialogDocument.Find(subDialogId) == null)
        {
            return EditResult.Failure($"'{subDialogId}' is not a SubDialog.");
        }

        next ??= "";
        if (next.Length > 0 && next != DialogSchema.EndCommand && !DialogDocument.Has(next))
        {
            return EditResult.Failure($"'{next}' is not a Dialog, <end>, or empty.");
        }

        SubDialogDocument.SetValue(subDialogId, DialogSchema.NextColumn, next);
        RebuildGraph();

        string description = next.Length == 0 ? "nothing" : next;
        return EditResult.Success($"{subDialogId} now leads to {description}.");
    }

    /// <summary>Sets the item a line hands over when it's clicked through, or clears it.</summary>
    public EditResult SetLineItemAward(string subDialogId, string itemId)
    {
        return SetLineItem(subDialogId, DialogSchema.ItemAwardColumn, itemId, "awards");
    }

    /// <summary>Sets the item a line is gated on, or clears it. Not read at runtime yet.</summary>
    public EditResult SetLineItemDependency(string subDialogId, string itemId)
    {
        return SetLineItem(subDialogId, DialogSchema.ItemDependencyColumn, itemId, "requires");
    }

    private EditResult SetLineItem(string subDialogId, string column, string itemId, string verb)
    {
        if (SubDialogDocument.Find(subDialogId) == null)
        {
            return EditResult.Failure($"'{subDialogId}' is not a SubDialog.");
        }

        itemId ??= "";
        if (itemId.Length > 0 && !ItemDocument.Has(itemId))
        {
            return EditResult.Failure($"'{itemId}' has no row in Item_Item.txt.");
        }

        SubDialogDocument.SetValue(subDialogId, column, itemId);
        RebuildGraph();

        return EditResult.Success(itemId.Length == 0
            ? $"{subDialogId} {verb} nothing."
            : $"{subDialogId} {verb} {itemId}.");
    }

    // ---- editing -----------------------------------------------------------------------

    public EditResult SetLineStyle(string subDialogId, string styleId)
    {
        if (!SubDialogDocument.SetValue(subDialogId, DialogSchema.StyleColumn, styleId))
        {
            return EditResult.Failure($"'{subDialogId}' is not a SubDialog.");
        }

        RebuildGraph();
        return EditResult.Success($"{subDialogId} styled {styleId}.");
    }

    public EditResult SetLineText(string subDialogId, string locale, string text)
    {
        var row = SubDialogDocument.Find(subDialogId);
        if (row == null)
        {
            return EditResult.Failure($"'{subDialogId}' is not a SubDialog.");
        }

        if (!TsvDocument.IsStorableValue(text))
        {
            return EditResult.Failure("Text can't contain tabs or line breaks.");
        }

        string localizationId = row.GetString(DialogSchema.LocalizationIdColumn);
        if (string.IsNullOrEmpty(localizationId))
        {
            return EditResult.Failure($"{subDialogId} has no LocalizationDialogID to write text into.");
        }

        // A line whose key was never added to Localization.tsv is exactly the case the validator
        // flags as missing-localization, so typing text here should fix it rather than refuse.
        bool created = !LocalizationDocument.Has(localizationId);
        if (created)
        {
            LocalizationDocument.AddRow(localizationId);
        }

        LocalizationDocument.SetValue(localizationId, locale, text);
        RebuildGraph();

        return EditResult.Success(created
            ? $"Created {localizationId} and set its {locale} text."
            : $"Updated {localizationId} [{locale}].");
    }

    public EditResult SetDialogAvatar(string dialogId, bool hasPortrait, int avatarIndex)
    {
        if (DialogDocument.Find(dialogId) == null)
        {
            return EditResult.Failure($"'{dialogId}' is not a Dialog.");
        }

        DialogDocument.SetValue(dialogId, DialogSchema.AvatarSheetColumn,
            hasPortrait ? DialogSchema.DefaultAvatarSheet : "");
        DialogDocument.SetValue(dialogId, DialogSchema.AvatarIndexColumn,
            hasPortrait ? avatarIndex.ToString() : "");

        RebuildGraph();
        return EditResult.Success(hasPortrait
            ? $"{dialogId} now uses avatar {avatarIndex}."
            : $"{dialogId} is now a narrator box.");
    }

    // ---- removal -----------------------------------------------------------------------

    /// <summary>
    /// Takes a line out of a Dialog's slots and closes the gap. The SubDialog row itself is only
    /// deleted if no other Dialog still shows it.
    /// </summary>
    public EditResult RemoveLine(string dialogId, string subDialogId)
    {
        var dialogRow = DialogDocument.Find(dialogId);
        if (dialogRow == null)
        {
            return EditResult.Failure($"'{dialogId}' is not a Dialog.");
        }

        var remaining = DialogSchema.SubDialogSlotColumns
            .Select(column => dialogRow.GetString(column))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        if (!remaining.Remove(subDialogId))
        {
            return EditResult.Failure($"{dialogId} doesn't show {subDialogId}.");
        }

        WriteSlots(dialogId, remaining);
        string cleanup = DeleteSubDialogIfUnused(subDialogId);

        RebuildGraph();
        return EditResult.Success($"Removed {subDialogId} from {dialogId}.{cleanup}");
    }

    /// <summary>
    /// Deletes a Dialog, the lines only it showed, and repoints anything that jumped to it. The
    /// inbound repoint is to &lt;end&gt; rather than to nothing, because a line left with an empty
    /// Next can leave its box with no way to close.
    /// </summary>
    public EditResult DeleteDialog(string dialogId)
    {
        var dialogRow = DialogDocument.Find(dialogId);
        if (dialogRow == null)
        {
            return EditResult.Failure($"'{dialogId}' is not a Dialog.");
        }

        var ownedLines = DialogSchema.SubDialogSlotColumns
            .Select(column => dialogRow.GetString(column))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        var inbound = SubDialogDocument.Rows
            .Where(row => row.GetString(DialogSchema.NextColumn) == dialogId)
            .Select(row => row.Id)
            .ToList();

        DialogDocument.RemoveRow(dialogId);

        foreach (string inboundId in inbound)
        {
            SubDialogDocument.SetValue(inboundId, DialogSchema.NextColumn, DialogSchema.EndCommand);
        }

        int deletedLines = 0;
        foreach (string line in ownedLines)
        {
            if (DeleteSubDialogIfUnused(line).Length > 0)
            {
                deletedLines++;
            }
        }

        RebuildGraph();

        var notes = new List<string> { $"Deleted {dialogId}" };
        if (deletedLines > 0)
        {
            notes.Add($"{deletedLines} line(s) it exclusively owned");
        }

        if (inbound.Count > 0)
        {
            notes.Add($"repointed {inbound.Count} inbound jump(s) to <end>");
        }

        return EditResult.Success(string.Join(", ", notes) + ".");
    }

    /// <summary>
    /// Drops a SubDialog row once no Dialog slots it any more. Its Localization row is
    /// deliberately left alone -- keys are shared across lines and also referenced by
    /// Item_Item.txt, so deleting one here could break a table this session never loaded.
    /// </summary>
    private string DeleteSubDialogIfUnused(string subDialogId)
    {
        bool stillUsed = DialogDocument.Rows.Any(row => DialogSchema.SubDialogSlotColumns
            .Any(column => row.GetString(column) == subDialogId));

        if (stillUsed || !SubDialogDocument.RemoveRow(subDialogId))
        {
            return "";
        }

        return $" Also deleted the now-unused {subDialogId} row.";
    }

    // Goes through SetValue rather than writing the row directly, so the document's dirty flag
    // actually sees the change -- SetValue treats an already-equal cell as a no-op.
    private void WriteSlots(string dialogId, IReadOnlyList<string> subDialogIds)
    {
        for (int slot = 0; slot < DialogSchema.SubDialogSlotColumns.Length; slot++)
        {
            string value = slot < subDialogIds.Count ? subDialogIds[slot] : "";
            DialogDocument.SetValue(dialogId, DialogSchema.SubDialogSlotColumns[slot], value);
        }
    }

    // ---- validation --------------------------------------------------------------------

    private static EditResult ValidateNewId(string id, TsvDocument document, string label)
    {
        if (string.IsNullOrEmpty(id))
        {
            return EditResult.Failure($"{label} id can't be empty.");
        }

        if (!TsvDocument.IsStorableValue(id))
        {
            return EditResult.Failure($"{label} id can't contain tabs or line breaks.");
        }

        return document.Has(id)
            ? EditResult.Failure($"{label} '{id}' already exists.")
            : EditResult.Success("");
    }

    private EditResult ValidateNewLine(NewLineRequest request)
    {
        if (request == null)
        {
            return EditResult.Failure("No line supplied.");
        }

        var idCheck = ValidateNewId(request.SubDialogId?.Trim() ?? "", SubDialogDocument, "SubDialog");
        if (!idCheck.Ok)
        {
            return idCheck;
        }

        string localizationId = request.LocalizationId?.Trim() ?? "";
        if (string.IsNullOrEmpty(localizationId))
        {
            return EditResult.Failure("Localization key can't be empty.");
        }

        if (!TsvDocument.IsStorableValue(localizationId) || !TsvDocument.IsStorableValue(request.EnglishText))
        {
            return EditResult.Failure("Key and text can't contain tabs or line breaks.");
        }

        string next = request.Next ?? "";
        if (next.Length > 0 && next != DialogSchema.EndCommand && !DialogDocument.Has(next))
        {
            return EditResult.Failure($"Next '{next}' is not a Dialog, <end>, or empty.");
        }

        foreach (string itemId in new[] { request.ItemAward, request.ItemDependency })
        {
            if (!string.IsNullOrEmpty(itemId) && !ItemDocument.Has(itemId))
            {
                return EditResult.Failure($"'{itemId}' has no row in Item_Item.txt.");
            }
        }

        return EditResult.Success("");
    }

    /// <summary>Suggests an unused id with the given prefix, e.g. "D.NewDialog2".</summary>
    public static string SuggestId(TsvDocument document, string prefix)
    {
        if (!document.Has(prefix))
        {
            return prefix;
        }

        for (int suffix = 2; suffix < 1000; suffix++)
        {
            if (!document.Has($"{prefix}{suffix}"))
            {
                return $"{prefix}{suffix}";
            }
        }

        return prefix;
    }
}
