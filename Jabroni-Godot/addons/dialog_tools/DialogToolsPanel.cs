using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Jabroni.Data;

namespace Jabroni.Editor;

/// <summary>
/// Authoring panel for the dialogue tables: shows the branching structure as a graph, validates
/// it continuously, and edits it in place. Every edit goes through a
/// <see cref="DialogEditSession"/> held in memory -- the TSV files are only touched by Save, so
/// Reload is always a complete undo.
/// </summary>
[Tool]
public partial class DialogToolsPanel : VBoxContainer
{
    private static readonly Color ErrorColor = new("ff7b6b");
    private static readonly Color WarningColor = new("ffd479");
    private static readonly Color OkColor = new("9fd97f");
    private static readonly Color DirtyColor = new("ffd479");

    private DialogEditSession _session;

    private Label _summary;
    private Label _status;
    private Tree _issues;
    private DialogGraphView _graphView;
    private DialogLineForm _form;
    private ConfirmationDialog _confirm;
    private Action _confirmedAction;

    private Button _saveButton;
    private Button _revertButton;
    private Button _addLineButton;
    private Button _editLineButton;
    private Button _removeLineButton;
    private Button _deleteDialogButton;
    private Button _portraitButton;

    private ConfirmationDialog _portraitForm;
    private CheckBox _portraitNarrator;
    private SpinBox _portraitIndex;

    // Diagnostics are keyed into the Tree by subject so a click can jump the graph to the Dialog
    // in question; SubDialog-level findings resolve to whichever Dialog shows that line.
    private readonly Dictionary<string, string> _dialogIdBySubject = new();

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 360);
        _session = DialogEditSession.Load();

        AddChild(BuildToolbar());
        AddChild(BuildBody());
        BuildPopups();

        RefreshFromSession();
    }

    private Control BuildToolbar()
    {
        var toolbar = new HBoxContainer();

        _saveButton = AddButton(toolbar, "Save", "Write the changed tables back to res://data/.", Save);
        _revertButton = AddButton(toolbar, "Reload", "Re-read the tables from disk, discarding unsaved edits.", ConfirmReload);

        toolbar.AddChild(new VSeparator());

        AddButton(toolbar, "New Dialog…", "Create a Dialog node and its first line.", OpenNewDialogForm);
        _addLineButton = AddButton(toolbar, "Add Line…", "Append a line to the selected Dialog.", OpenAddLineForm);
        _editLineButton = AddButton(toolbar, "Edit Line…", "Change the selected line's text, style, pitch or Next.", OpenEditLineForm);
        _removeLineButton = AddButton(toolbar, "Remove Line", "Take the selected line out of its Dialog.", ConfirmRemoveLine);
        _deleteDialogButton = AddButton(toolbar, "Delete Dialog", "Delete the selected Dialog node.", ConfirmDeleteSelectedDialog);
        _portraitButton = AddButton(toolbar, "Portrait…", "Change the selected Dialog's speaker portrait.", OpenPortraitForm);

        toolbar.AddChild(new VSeparator());

        AddButton(toolbar, "Copy Mermaid", "Copy the graph to the clipboard as a Mermaid flowchart.", CopyMermaid);
        AddButton(toolbar, "Print Report", "Write the full validation report to the Output panel.", PrintReport);

        toolbar.AddChild(new VSeparator());

        _summary = new Label { VerticalAlignment = VerticalAlignment.Center };
        toolbar.AddChild(_summary);

        _status = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Right,
            ClipText = true,
        };
        toolbar.AddChild(_status);

        return toolbar;
    }

    private Control BuildBody()
    {
        var split = new HSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SplitOffsets = new[] { 420 },
        };

        _issues = new Tree
        {
            HideRoot = true,
            Columns = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AllowReselect = true,
        };
        _issues.ItemSelected += OnIssueSelected;
        split.AddChild(_issues);

        _graphView = new DialogGraphView
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ShowGrid = true,
            MinimapEnabled = false,
        };
        _graphView.LinkRequested += (subDialogId, target) => Apply(_session.SetNext(subDialogId, target));
        _graphView.UnlinkRequested += subDialogId => Apply(_session.SetNext(subDialogId, ""));
        _graphView.DeleteDialogsRequested += ConfirmDeleteDialogs;
        _graphView.AddDialogRequested += OpenNewDialogForm;
        _graphView.SelectionChanged += (_, _) => UpdateButtonStates();
        split.AddChild(_graphView);

        return split;
    }

    private void BuildPopups()
    {
        _form = new DialogLineForm();
        _form.Submitted += OnFormSubmitted;
        AddChild(_form);

        _confirm = new ConfirmationDialog();
        _confirm.Confirmed += () => _confirmedAction?.Invoke();
        AddChild(_confirm);

        BuildPortraitForm();
    }

    // The portrait is a property of the Dialog rather than of any one line, so it gets its own
    // two-field popup instead of riding along in the line form.
    private void BuildPortraitForm()
    {
        _portraitForm = new ConfirmationDialog { Title = "Portrait", OkButtonText = "Apply" };

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        _portraitForm.AddChild(grid);

        grid.AddChild(new Label());
        _portraitNarrator = new CheckBox { Text = "Narrator (no portrait)" };
        grid.AddChild(_portraitNarrator);

        grid.AddChild(new Label { Text = "Avatar index" });
        _portraitIndex = new SpinBox { MinValue = 0, MaxValue = 15, Step = 1 };
        grid.AddChild(_portraitIndex);

        _portraitNarrator.Toggled += pressed => _portraitIndex.Editable = !pressed;
        _portraitForm.Confirmed += OnPortraitConfirmed;
        AddChild(_portraitForm);
    }

    private void OpenPortraitForm()
    {
        string dialogId = _graphView.SelectedDialogId;
        if (dialogId == null || !_session.Graph.Dialogs.TryGetValue(dialogId, out var dialog))
        {
            return;
        }

        _portraitForm.Title = $"Portrait for {dialogId}";
        _portraitNarrator.ButtonPressed = !dialog.HasPortrait;
        _portraitIndex.Editable = dialog.HasPortrait;
        _portraitIndex.Value = Mathf.Max(dialog.AvatarIndex, 0);
        _portraitForm.PopupCentered();
    }

    private void OnPortraitConfirmed()
    {
        string dialogId = _graphView.SelectedDialogId;
        if (dialogId != null)
        {
            Apply(_session.SetDialogAvatar(dialogId, !_portraitNarrator.ButtonPressed, (int)_portraitIndex.Value));
        }
    }

    // ---- refresh -----------------------------------------------------------------------

    /// <summary>Redraws everything from the session's in-memory graph. No disk access.</summary>
    private void RefreshFromSession()
    {
        var diagnostics = DialogGraphValidator.Validate(_session.Graph);

        BuildSubjectIndex();
        PopulateIssues(diagnostics);
        _graphView.Rebuild(_session.Graph);
        UpdateSummary(diagnostics);
        UpdateButtonStates();
    }

    private void Apply(EditResult result)
    {
        SetStatus(result.Message, result.Ok ? OkColor : ErrorColor);
        if (result.Ok)
        {
            RefreshFromSession();
        }
    }

    private void UpdateButtonStates()
    {
        bool hasDialog = _graphView.SelectedDialogId != null;
        bool hasLine = _graphView.SelectedSubDialogId != null;

        _saveButton.Disabled = !_session.IsDirty;
        _revertButton.Text = _session.IsDirty ? "Reload (discard)" : "Reload";
        _addLineButton.Disabled = !hasDialog;
        _editLineButton.Disabled = !hasLine;
        _removeLineButton.Disabled = !hasLine;
        _deleteDialogButton.Disabled = !hasDialog;
        _portraitButton.Disabled = !hasDialog;
    }

    private void UpdateSummary(IReadOnlyList<DialogDiagnostic> diagnostics)
    {
        var graph = _session.Graph;
        int errors = diagnostics.Count(d => d.Severity == DialogDiagnosticSeverity.Error);
        int warnings = diagnostics.Count - errors;
        int unreachable = graph.Dialogs.Count - graph.ReachableDialogIds().Count;

        string dirty = _session.IsDirty ? "●  unsaved  ·  " : "";
        _summary.Text = $"{dirty}{graph.Dialogs.Count} dialogs · {graph.SubDialogIds.Count} lines · "
                        + $"{graph.EntryPoints.Count} entry points · {unreachable} unreachable · "
                        + $"{errors} errors, {warnings} warnings";

        _summary.Modulate = _session.IsDirty ? DirtyColor
            : errors > 0 ? ErrorColor
            : warnings > 0 ? WarningColor
            : OkColor;
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.Modulate = color;
    }

    // ---- issues ------------------------------------------------------------------------

    private void BuildSubjectIndex()
    {
        _dialogIdBySubject.Clear();

        foreach (var dialog in _session.Graph.Dialogs.Values)
        {
            _dialogIdBySubject[dialog.DialogId] = dialog.DialogId;

            foreach (var line in dialog.Lines)
            {
                _dialogIdBySubject.TryAdd(line.SubDialogId, dialog.DialogId);
            }
        }

        foreach (var entry in _session.Graph.EntryPoints)
        {
            _dialogIdBySubject.TryAdd(entry.SourceId, entry.DialogId);
        }
    }

    private void PopulateIssues(IReadOnlyList<DialogDiagnostic> diagnostics)
    {
        _issues.Clear();
        var root = _issues.CreateItem();

        AddSeverityGroup(root, diagnostics, DialogDiagnosticSeverity.Error, "Errors", ErrorColor);
        AddSeverityGroup(root, diagnostics, DialogDiagnosticSeverity.Warning, "Warnings", WarningColor);

        if (diagnostics.Count == 0)
        {
            var clean = _issues.CreateItem(root);
            clean.SetText(0, "No problems found.");
            clean.SetCustomColor(0, OkColor);
        }
    }

    private void AddSeverityGroup(
        TreeItem root,
        IReadOnlyList<DialogDiagnostic> diagnostics,
        DialogDiagnosticSeverity severity,
        string label,
        Color color)
    {
        var matching = diagnostics.Where(d => d.Severity == severity).ToList();
        if (matching.Count == 0)
        {
            return;
        }

        var group = _issues.CreateItem(root);
        group.SetText(0, $"{label} ({matching.Count})");
        group.SetCustomColor(0, color);
        group.Collapsed = false;

        foreach (var diagnostic in matching)
        {
            var item = _issues.CreateItem(group);
            item.SetText(0, $"{diagnostic.Subject} — {diagnostic.Message}");
            item.SetTooltipText(0, $"[{diagnostic.Code}] {diagnostic.Subject}\n\n{diagnostic.Message}");
            item.SetMetadata(0, diagnostic.Subject);
        }
    }

    private void OnIssueSelected()
    {
        var selected = _issues.GetSelected();
        var metadata = selected?.GetMetadata(0);
        if (metadata.HasValue && metadata.Value.VariantType == Variant.Type.String
            && _dialogIdBySubject.TryGetValue(metadata.Value.AsString(), out string dialogId))
        {
            _graphView.FocusDialog(dialogId);
        }
    }

    // ---- editing -----------------------------------------------------------------------

    private void OpenNewDialogForm() => _form.OpenForNewDialog(_session);

    private void OpenAddLineForm()
    {
        if (_graphView.SelectedDialogId != null)
        {
            _form.OpenForNewLine(_session, _graphView.SelectedDialogId);
        }
    }

    private void OpenEditLineForm()
    {
        if (_graphView.SelectedDialogId != null && _graphView.SelectedSubDialogId != null)
        {
            _form.OpenForEditLine(_session, _graphView.SelectedDialogId, _graphView.SelectedSubDialogId);
        }
    }

    private void OnFormSubmitted(DialogFormResult result)
    {
        switch (result.Mode)
        {
            case DialogFormMode.NewDialog:
                Apply(_session.CreateDialog(result.Dialog));
                break;

            case DialogFormMode.NewLine:
                Apply(_session.AddLine(result.TargetDialogId, result.Line));
                break;

            case DialogFormMode.EditLine:
                ApplyLineEdits(result);
                break;
        }
    }

    // Three independent writes; the first failure wins so the author sees the actual complaint
    // rather than a later success overwriting it in the status line.
    private void ApplyLineEdits(DialogFormResult result)
    {
        string subDialogId = result.EditingSubDialogId;

        var edits = new[]
        {
            _session.SetLineText(subDialogId, DialogSchema.PreviewLocale, result.Line.EnglishText),
            _session.SetLineStyle(subDialogId, result.Line.StyleId),
            _session.SetNext(subDialogId, result.Line.Next),
        };

        var failures = edits.Where(edit => !edit.Ok).ToList();
        Apply(failures.Count > 0 ? failures[0] : EditResult.Success($"Updated {subDialogId}."));
    }

    private void ConfirmRemoveLine()
    {
        string dialogId = _graphView.SelectedDialogId;
        string subDialogId = _graphView.SelectedSubDialogId;
        if (dialogId == null || subDialogId == null)
        {
            return;
        }

        AskThen($"Remove {subDialogId} from {dialogId}?\n\n"
                + "If no other Dialog shows this line, its SubDialog row is deleted too. "
                + "Its Localization row is left in place.",
            () => Apply(_session.RemoveLine(dialogId, subDialogId)));
    }

    private void ConfirmDeleteSelectedDialog()
    {
        if (_graphView.SelectedDialogId != null)
        {
            ConfirmDeleteDialogs(new[] { _graphView.SelectedDialogId });
        }
    }

    private void ConfirmDeleteDialogs(IReadOnlyList<string> dialogIds)
    {
        AskThen($"Delete {string.Join(", ", dialogIds)}?\n\n"
                + "Lines only these Dialogs showed are deleted too, and anything that jumped to "
                + "them is repointed to <end>. Nothing is written until you press Save.",
            () =>
            {
                foreach (string dialogId in dialogIds)
                {
                    var result = _session.DeleteDialog(dialogId);
                    if (!result.Ok)
                    {
                        Apply(result);
                        return;
                    }

                    SetStatus(result.Message, OkColor);
                }

                RefreshFromSession();
            });
    }

    // ---- file operations ---------------------------------------------------------------

    private void Save() => Apply(_session.Save());

    private void ConfirmReload()
    {
        if (!_session.IsDirty)
        {
            ReloadFromDisk();
            return;
        }

        AskThen("Discard unsaved dialogue edits and re-read the tables from disk?", ReloadFromDisk);
    }

    private void ReloadFromDisk()
    {
        _session.Reload();
        RefreshFromSession();
        SetStatus("Reloaded from disk.", OkColor);
    }

    private void AskThen(string message, Action action)
    {
        _confirmedAction = action;
        _confirm.DialogText = message;
        _confirm.PopupCentered();
    }

    private void CopyMermaid()
    {
        DisplayServer.ClipboardSet(DialogGraphMermaid.Export(_session.Graph));
        SetStatus("Mermaid flowchart copied to clipboard.", OkColor);
    }

    private void PrintReport()
    {
        var diagnostics = DialogGraphValidator.Validate(_session.Graph);
        GD.Print($"--- Dialogue validation: {_session.Graph.Dialogs.Count} dialogs, "
                 + $"{diagnostics.Count} findings ---");

        foreach (var diagnostic in diagnostics)
        {
            GD.Print($"  [{diagnostic.Severity}] {diagnostic.Subject}: {diagnostic.Message}");
        }

        if (diagnostics.Count == 0)
        {
            GD.Print("  No problems found.");
        }

        SetStatus("Report written to the Output panel.", OkColor);
    }

    private static Button AddButton(Control parent, string text, string tooltip, Action pressed)
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }
}
