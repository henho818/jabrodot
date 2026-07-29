using System;
using System.Collections.Generic;
using Godot;
using Jabroni.Data;

namespace Jabroni.Editor;

public enum DialogFormMode
{
    NewDialog,
    NewLine,
    EditLine,
}

/// <summary>What the form collected, for the panel to turn into edit-session calls.</summary>
public sealed class DialogFormResult
{
    public DialogFormMode Mode { get; init; }
    public NewDialogRequest Dialog { get; init; }
    public NewLineRequest Line { get; init; }

    /// <summary>The Dialog being added to, in <see cref="DialogFormMode.NewLine"/>.</summary>
    public string TargetDialogId { get; init; }

    /// <summary>The line being changed, in <see cref="DialogFormMode.EditLine"/>.</summary>
    public string EditingSubDialogId { get; init; }
}

/// <summary>
/// The one authoring form, in three modes: create a Dialog (which always comes with its first
/// line, so a new node is never born empty and unclosable), append a line to an existing Dialog,
/// or edit a line already in the graph.
/// <para>
/// It only collects and shapes a request -- <see cref="DialogEditSession"/> does the validating,
/// so the form can't accept something the data layer would reject.
/// </para>
/// </summary>
[Tool]
public partial class DialogLineForm : ConfirmationDialog
{
    private const string NoNextLabel = "(nothing — statement line)";
    private const string NoItemLabel = "(none)";

    public event Action<DialogFormResult> Submitted;

    private readonly Dictionary<Control, Label> _labels = new();

    private LineEdit _dialogId;
    private CheckBox _narrator;
    private SpinBox _avatarIndex;
    private LineEdit _subDialogId;
    private LineEdit _localizationId;
    private LineEdit _englishText;
    private OptionButton _style;
    private OptionButton _next;
    private OptionButton _itemAward;
    private OptionButton _itemDependency;
    private SpinBox _pitch;

    private DialogFormMode _mode;
    private string _targetDialogId;
    private string _editingSubDialogId;
    private bool _localizationIdEdited;

    public override void _Ready()
    {
        MinSize = new Vector2I(560, 0);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 6);
        AddChild(grid);

        _dialogId = AddLineEdit(grid, "Dialog id");
        _narrator = AddCheckBox(grid, "Narrator (no portrait)");
        _avatarIndex = AddSpinBox(grid, "Avatar index", 0, 15, 1);
        AddSeparator(grid);
        _subDialogId = AddLineEdit(grid, "Line id");
        _localizationId = AddLineEdit(grid, "Localization key");
        _englishText = AddLineEdit(grid, "English text");
        _style = AddOptionButton(grid, "Style");
        _next = AddOptionButton(grid, "Next");

        _itemAward = AddOptionButton(grid, "Awards item");
        _itemAward.TooltipText = "Handed to the player on the click that advances this line. "
                                 + "A line with no Next never advances, so it would never be given.";

        _itemDependency = AddOptionButton(grid, "Requires item");
        _itemDependency.TooltipText = "Authoring only for now -- DialogBox does not read "
                                      + "ItemDependency, so this gates nothing at runtime yet.";

        _pitch = AddSpinBox(grid, "Pitch (semitones)", -12, 12, 1);

        // The key is derived from the line id (S.Foo -> LD.Foo) until the author overrides it,
        // which covers the common case without locking anyone out of reusing an existing key.
        _subDialogId.TextChanged += _ => SyncLocalizationId();
        _localizationId.TextChanged += _ => _localizationIdEdited = true;
        _narrator.Toggled += pressed => _avatarIndex.Editable = !pressed;

        Confirmed += OnConfirmed;
    }

    public void OpenForNewDialog(DialogEditSession session)
    {
        _mode = DialogFormMode.NewDialog;
        _targetDialogId = null;
        _editingSubDialogId = null;

        Title = "New Dialog";
        OkButtonText = "Create Dialog";

        SetDialogFieldsVisible(true);
        SetLineIdsEditable(true);

        _dialogId.Text = DialogEditSession.SuggestId(session.DialogDocument, "D.NewDialog");
        _narrator.ButtonPressed = false;
        _avatarIndex.Editable = true;
        _avatarIndex.Value = 0;

        ResetLineFields(session, DialogEditSession.SuggestId(session.SubDialogDocument, "S.NewLine"));
        PopupAndFocus(_dialogId);
    }

    public void OpenForNewLine(DialogEditSession session, string dialogId)
    {
        _mode = DialogFormMode.NewLine;
        _targetDialogId = dialogId;
        _editingSubDialogId = null;

        Title = $"Add Line to {dialogId}";
        OkButtonText = "Add Line";

        SetDialogFieldsVisible(false);
        SetLineIdsEditable(true);

        ResetLineFields(session, DialogEditSession.SuggestId(session.SubDialogDocument, "S.NewLine"));
        PopupAndFocus(_englishText);
    }

    /// <summary>
    /// Opens the form on an existing line. Its ids are shown but locked -- renaming a SubDialog
    /// would have to chase every Dialog slot that references it, which this tool doesn't do.
    /// </summary>
    public void OpenForEditLine(DialogEditSession session, string dialogId, string subDialogId)
    {
        var row = session.SubDialogDocument.Find(subDialogId);
        if (row == null)
        {
            return;
        }

        _mode = DialogFormMode.EditLine;
        _targetDialogId = dialogId;
        _editingSubDialogId = subDialogId;

        Title = $"Edit {subDialogId}";
        OkButtonText = "Apply";

        SetDialogFieldsVisible(false);
        SetLineIdsEditable(false);
        PopulateChoices(session);

        string localizationId = row.GetString(DialogSchema.LocalizationIdColumn);
        _subDialogId.Text = subDialogId;
        _localizationId.Text = localizationId;
        _englishText.Text = session.LocalizationDocument.Find(localizationId)
            ?.GetString(DialogSchema.PreviewLocale) ?? "";
        _pitch.Value = row.GetFloat(DialogSchema.PitchColumn, 2f);

        SelectItem(_style, row.GetString(DialogSchema.StyleColumn), 0);

        string next = row.GetString(DialogSchema.NextColumn);
        SelectItem(_next, string.IsNullOrEmpty(next) ? NoNextLabel : next, 0);

        SelectItem(_itemAward, row.GetString(DialogSchema.ItemAwardColumn), 0);
        SelectItem(_itemDependency, row.GetString(DialogSchema.ItemDependencyColumn), 0);

        PopupAndFocus(_englishText);
    }

    private void PopupAndFocus(Control field)
    {
        PopupCentered();
        field.GrabFocus();
    }

    private void ResetLineFields(DialogEditSession session, string suggestedLineId)
    {
        _localizationIdEdited = false;
        _subDialogId.Text = suggestedLineId;
        SyncLocalizationId();
        _englishText.Text = "";
        _pitch.Value = 2;

        PopulateChoices(session);

        // A line that closes the box is the safest default: it can't leave a Dialog with no exit.
        SelectItem(_next, DialogSchema.EndCommand, 1);

        _itemAward.Selected = 0;
        _itemDependency.Selected = 0;
    }

    private void PopulateChoices(DialogEditSession session)
    {
        _style.Clear();
        foreach (string styleId in session.StyleIds)
        {
            _style.AddItem(styleId);
        }

        _next.Clear();
        _next.AddItem(NoNextLabel);
        _next.AddItem(DialogSchema.EndCommand);
        foreach (string dialogId in session.DialogIds)
        {
            _next.AddItem(dialogId);
        }

        PopulateItemChoices(_itemAward, session);
        PopulateItemChoices(_itemDependency, session);
    }

    /// <summary>
    /// Fills an item dropdown with "(none)" plus every item id. The id is the item text -- it is
    /// what the column stores, and what <see cref="SelectItem"/> matches on -- with the readable
    /// name carried in the per-item tooltip so the list stays honest about what it writes.
    /// </summary>
    private static void PopulateItemChoices(OptionButton option, DialogEditSession session)
    {
        option.Clear();
        option.AddItem(NoItemLabel);

        foreach (string itemId in session.ItemIds)
        {
            option.AddItem(itemId);
            option.SetItemTooltip(option.ItemCount - 1, session.ItemDisplayName(itemId));
        }
    }

    private static void SelectItem(OptionButton option, string text, int fallbackIndex)
    {
        for (int i = 0; i < option.ItemCount; i++)
        {
            if (option.GetItemText(i) == text)
            {
                option.Selected = i;
                return;
            }
        }

        option.Selected = Mathf.Min(fallbackIndex, option.ItemCount - 1);
    }

    private void SyncLocalizationId()
    {
        if (_localizationIdEdited)
        {
            return;
        }

        string lineId = _subDialogId.Text.Trim();
        string stem = lineId.StartsWith("S.") ? lineId[2..] : lineId;
        _localizationId.Text = string.IsNullOrEmpty(stem) ? "" : $"LD.{stem}";
        _localizationIdEdited = false;
    }

    private void SetDialogFieldsVisible(bool visible)
    {
        foreach (var control in new Control[] { _dialogId, _narrator, _avatarIndex })
        {
            control.Visible = visible;
            _labels[control].Visible = visible;
        }
    }

    private void SetLineIdsEditable(bool editable)
    {
        _subDialogId.Editable = editable;
        _localizationId.Editable = editable;
    }

    private void OnConfirmed()
    {
        var line = new NewLineRequest
        {
            SubDialogId = _subDialogId.Text.Trim(),
            LocalizationId = _localizationId.Text.Trim(),
            EnglishText = _englishText.Text,
            StyleId = _style.Selected >= 0 ? _style.GetItemText(_style.Selected) : "",
            Next = _next.Selected <= 0 ? "" : _next.GetItemText(_next.Selected),
            ItemAward = SelectedItemId(_itemAward),
            ItemDependency = SelectedItemId(_itemDependency),
            Pitch = (float)_pitch.Value,
        };

        Submitted?.Invoke(new DialogFormResult
        {
            Mode = _mode,
            Line = line,
            TargetDialogId = _targetDialogId,
            EditingSubDialogId = _editingSubDialogId,
            Dialog = _mode != DialogFormMode.NewDialog ? null : new NewDialogRequest
            {
                DialogId = _dialogId.Text.Trim(),
                HasPortrait = !_narrator.ButtonPressed,
                AvatarIndex = (int)_avatarIndex.Value,
                FirstLine = line,
            },
        });
    }

    /// <summary>Index 0 is always "(none)", which the column stores as an empty cell.</summary>
    private static string SelectedItemId(OptionButton option)
    {
        return option.Selected <= 0 ? "" : option.GetItemText(option.Selected);
    }

    // ---- tiny form builders ------------------------------------------------------------

    private T AddField<T>(GridContainer grid, string label, T control) where T : Control
    {
        var labelNode = new Label { Text = label };
        grid.AddChild(labelNode);
        grid.AddChild(control);
        _labels[control] = labelNode;
        return control;
    }

    private LineEdit AddLineEdit(GridContainer grid, string label)
    {
        return AddField(grid, label, new LineEdit { CustomMinimumSize = new Vector2(360, 0) });
    }

    private CheckBox AddCheckBox(GridContainer grid, string label)
    {
        return AddField(grid, "", new CheckBox { Text = label });
    }

    private SpinBox AddSpinBox(GridContainer grid, string label, double min, double max, double step)
    {
        return AddField(grid, label, new SpinBox { MinValue = min, MaxValue = max, Step = step });
    }

    private OptionButton AddOptionButton(GridContainer grid, string label)
    {
        return AddField(grid, label, new OptionButton());
    }

    private static void AddSeparator(GridContainer grid)
    {
        grid.AddChild(new HSeparator());
        grid.AddChild(new HSeparator());
    }
}
