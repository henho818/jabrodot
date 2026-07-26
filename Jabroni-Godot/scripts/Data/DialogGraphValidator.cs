using System.Collections.Generic;
using System.Linq;
using Jabroni.UI.Dialog;

namespace Jabroni.Data;

public enum DialogDiagnosticSeverity
{
    /// <summary>The dialogue is broken at runtime: a line won't show, a jump won't land, or the box can't be closed.</summary>
    Error,

    /// <summary>The dialogue still runs, but something is almost certainly an authoring mistake.</summary>
    Warning,
}

/// <summary>One validation finding, addressed to the row the author needs to go fix.</summary>
public sealed class DialogDiagnostic
{
    public DialogDiagnosticSeverity Severity { get; init; }

    /// <summary>Stable short code (e.g. "unknown-next") so findings can be grouped or filtered.</summary>
    public string Code { get; init; }

    /// <summary>Which table row this is about, e.g. "D.TestSpeech" or "S.OK_TestThought".</summary>
    public string Subject { get; init; }

    public string Message { get; init; }

    public override string ToString() => $"{Subject}: {Message}";
}

/// <summary>
/// Checks a <see cref="DialogGraph"/> for the reference mistakes that TSV authoring invites --
/// ids that don't resolve, localization keys that were never written, branches nothing can
/// reach. Every check mirrors a specific runtime behaviour in DialogBox, so a clean report
/// means the box will actually do what the tables say.
/// </summary>
public static class DialogGraphValidator
{
    public static List<DialogDiagnostic> Validate(DialogGraph graph)
    {
        var diagnostics = new List<DialogDiagnostic>();

        ValidateEntryPoints(graph, diagnostics);

        foreach (var dialog in graph.Dialogs.Values.OrderBy(d => d.DialogId))
        {
            ValidateDialog(graph, dialog, diagnostics);
        }

        ValidateOrphanSubDialogs(graph, diagnostics);
        ValidateUnreachableDialogs(graph, diagnostics);

        return diagnostics
            .OrderBy(d => d.Severity)
            .ThenBy(d => d.Subject)
            .ToList();
    }

    private static void ValidateEntryPoints(DialogGraph graph, List<DialogDiagnostic> diagnostics)
    {
        foreach (var entry in graph.EntryPoints.Where(entry => !graph.Dialogs.ContainsKey(entry.DialogId)))
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Error,
                Code = "unknown-entry-dialog",
                Subject = entry.ConfigId,
                Message = $"DialogId '{entry.DialogId}' has no row in Dialog_Dialog.txt -- "
                          + $"talking to {Describe(entry.AgentName)} would open nothing.",
            });
        }
    }

    private static void ValidateDialog(DialogGraph graph, DialogNode dialog, List<DialogDiagnostic> diagnostics)
    {
        // A Dialog whose slots are all empty or all unresolvable opens a zero-line box that then
        // has nothing clickable in it -- the player is stuck with a blank panel on screen.
        if (dialog.Lines.Count == 0)
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Error,
                Code = "empty-dialog",
                Subject = dialog.DialogId,
                Message = "No SubDialog slots are filled -- triggering this opens an empty box.",
            });
        }

        ValidatePortrait(dialog, diagnostics);

        foreach (var line in dialog.Lines)
        {
            ValidateLine(graph, dialog, line, diagnostics);
        }

        ValidateNoWayOut(dialog, diagnostics);
    }

    private static void ValidatePortrait(DialogNode dialog, List<DialogDiagnostic> diagnostics)
    {
        // Asks the same lookup DialogBox.UpdatePortrait uses rather than hardcoding the sheet's
        // 0-15 range here, so re-slicing avatars.png can't leave the validator out of date.
        if (dialog.HasPortrait && AvatarSheet.GetCellRect(dialog.AvatarIndex) == null)
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Error,
                Code = "bad-avatar-index",
                Subject = dialog.DialogId,
                Message = $"AvatarSheet is '{dialog.AvatarSheet}' but AvatarIndex {dialog.AvatarIndex} "
                          + "is not a cell in the sheet -- the portrait is silently hidden.",
            });
        }
    }

    private static void ValidateLine(
        DialogGraph graph,
        DialogNode dialog,
        DialogLineNode line,
        List<DialogDiagnostic> diagnostics)
    {
        string slot = DialogSchema.SubDialogSlotColumns[line.SlotIndex];

        if (!line.Resolved)
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Error,
                Code = "unknown-subdialog",
                Subject = dialog.DialogId,
                Message = $"{slot} names '{line.SubDialogId}', which has no row in Dialog_SubDialog.txt "
                          + "-- the line is skipped.",
            });
            return;
        }

        if (line.LinkKind == DialogLinkKind.Jump && !graph.Dialogs.ContainsKey(line.NextDialogId))
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Error,
                Code = "unknown-next",
                Subject = line.SubDialogId,
                Message = $"Next is '{line.RawNext}', which has no row in Dialog_Dialog.txt "
                          + $"-- clicking this line in {dialog.DialogId} warns and does nothing.",
            });
        }

        if (!string.IsNullOrEmpty(line.StyleId) && !graph.StyleIds.Contains(line.StyleId))
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Error,
                Code = "unknown-style",
                Subject = line.SubDialogId,
                Message = $"Style '{line.StyleId}' has no row in Dialog_SubDialogStyle.txt "
                          + "-- the line falls back to black-on-white.",
            });
        }

        ValidateLocalization(graph, line, diagnostics);
    }

    private static void ValidateLocalization(DialogGraph graph, DialogLineNode line, List<DialogDiagnostic> diagnostics)
    {
        if (string.IsNullOrEmpty(line.LocalizationId))
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Warning,
                Code = "no-localization-id",
                Subject = line.SubDialogId,
                Message = "LocalizationDialogID is blank -- the line renders as an empty box.",
            });
            return;
        }

        if (!graph.Localization.TryGetValue(line.LocalizationId, out var translations))
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Warning,
                Code = "missing-localization",
                Subject = line.SubDialogId,
                Message = $"'{line.LocalizationId}' is not in Localization.tsv -- Tr() falls through "
                          + "and the raw key is shown to the player.",
            });
            return;
        }

        // Untranslated locales aren't fatal (TranslationServer falls back), but they're the thing
        // that quietly ships as English text in a Japanese build, so they're worth surfacing.
        var missingLocales = translations
            .Where(pair => string.IsNullOrEmpty(pair.Value))
            .Select(pair => pair.Key)
            .ToList();

        if (missingLocales.Count > 0)
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Warning,
                Code = "untranslated",
                Subject = line.SubDialogId,
                Message = $"'{line.LocalizationId}' has no text for: {string.Join(", ", missingLocales)}.",
            });
        }
    }

    // DialogBox only closes on a line whose Next is <end>, and only swaps boxes on a line with a
    // Next. A Dialog where every line has an empty Next is therefore a soft-lock: the box is up,
    // clicks land on lines that go nowhere, and nothing in the dialogue system can dismiss it.
    private static void ValidateNoWayOut(DialogNode dialog, List<DialogDiagnostic> diagnostics)
    {
        if (dialog.Lines.Count == 0)
        {
            return;
        }

        bool hasExit = dialog.Lines.Any(line => line.LinkKind is DialogLinkKind.End or DialogLinkKind.Jump);
        if (!hasExit)
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Error,
                Code = "no-way-out",
                Subject = dialog.DialogId,
                Message = "No line has a Next or <end> -- once this box opens the player can never "
                          + "close it (only walking out of detection range will).",
            });
        }
    }

    private static void ValidateOrphanSubDialogs(DialogGraph graph, List<DialogDiagnostic> diagnostics)
    {
        var used = graph.UsedSubDialogIds();

        foreach (string subDialogId in graph.SubDialogIds.Except(used).OrderBy(id => id))
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Warning,
                Code = "orphan-subdialog",
                Subject = subDialogId,
                Message = "Not placed in any Dialog's SubDialogID slots -- dead data.",
            });
        }
    }

    private static void ValidateUnreachableDialogs(DialogGraph graph, List<DialogDiagnostic> diagnostics)
    {
        var reachable = graph.ReachableDialogIds();

        foreach (string dialogId in graph.Dialogs.Keys.Except(reachable).OrderBy(id => id))
        {
            diagnostics.Add(new DialogDiagnostic
            {
                Severity = DialogDiagnosticSeverity.Warning,
                Code = "unreachable-dialog",
                Subject = dialogId,
                Message = "No agent config starts here and no Next leads here -- unreachable in game.",
            });
        }
    }

    private static string Describe(string agentName)
    {
        return string.IsNullOrEmpty(agentName) ? "this agent" : agentName;
    }
}
