using Godot;

namespace Jabroni.Editor;

/// <summary>
/// The Inspector editor for AgentAI.ChatDialogId: a free-text field, a dropdown of the Dialog
/// ids that actually exist, and red text when what's typed isn't one of them.
/// <para>
/// It stays a text field rather than becoming a plain enum because an id can legitimately be
/// typed before the Dialog is authored -- the red is a warning, not a refusal. Blank is valid
/// and means the agent opens no box at all.
/// </para>
/// <para>
/// This is an <see cref="EditorProperty"/> driven by <see cref="DialogInspectorPlugin"/> rather
/// than a hint on the export itself, because a hint would have to come from AgentAI running in
/// the editor -- which needs [Tool] on every concrete subclass -- and could not colour the
/// field regardless.
/// </para>
/// </summary>
[Tool]
public partial class ChatDialogIdProperty : EditorProperty
{
    /// <summary>Menu id for the entry that clears the field; real ids are offset past it.</summary>
    private const int ClearId = 0;
    private const int FirstDialogId = 1;

    private readonly LineEdit _text = new();
    private readonly Button _pick = new();
    private readonly PopupMenu _choices = new();

    /// <summary>Guards against treating a write from the edited object as if the author typed it.</summary>
    private bool _updatingFromProperty;

    public ChatDialogIdProperty()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);

        _text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _text.PlaceholderText = "(no dialogue)";
        _text.TextChanged += OnTextChanged;

        _pick.Flat = true;
        _pick.TooltipText = "Pick a Dialog from Dialog_Dialog.txt";
        _pick.Pressed += OnPickPressed;

        _choices.IdPressed += OnChoicePressed;

        row.AddChild(_text);
        row.AddChild(_pick);
        AddChild(row);
        AddChild(_choices);
        AddFocusable(_text);
    }

    public override void _Ready()
    {
        _pick.Icon = GetThemeIcon("arrow", "OptionButton");
    }

    public override void _UpdateProperty()
    {
        string dialogId = GetEditedObject()?.Get(GetEditedProperty()).AsString() ?? "";

        // Only write the field when it actually differs: _UpdateProperty runs after every
        // EmitChanged, and reassigning Text mid-typing would send the caret back to the start.
        if (_text.Text != dialogId)
        {
            _updatingFromProperty = true;
            _text.Text = dialogId;
            _updatingFromProperty = false;
        }

        ShowValidity(dialogId);
    }

    private void OnTextChanged(string dialogId)
    {
        if (_updatingFromProperty)
        {
            return;
        }

        ShowValidity(dialogId);
        EmitChanged(GetEditedProperty(), dialogId);
    }

    private void OnPickPressed()
    {
        _choices.Clear();
        _choices.AddItem("(none)", ClearId);
        _choices.AddSeparator();

        var ids = DialogIdCatalog.Ids;
        for (int index = 0; index < ids.Count; index++)
        {
            _choices.AddItem(ids[index], FirstDialogId + index);
        }

        // Dropped directly under the field and matched to its width, so it reads as that
        // field's list rather than as a menu belonging to the little arrow button.
        var origin = (Vector2I)_text.GetScreenPosition() + new Vector2I(0, (int)_text.Size.Y);
        _choices.Popup(new Rect2I(origin, new Vector2I((int)_text.Size.X, 0)));
    }

    private void OnChoicePressed(long choice)
    {
        var ids = DialogIdCatalog.Ids;
        int index = (int)choice - FirstDialogId;

        string dialogId = choice == ClearId || index < 0 || index >= ids.Count ? "" : ids[index];

        _text.Text = dialogId;
        ShowValidity(dialogId);
        EmitChanged(GetEditedProperty(), dialogId);
    }

    /// <summary>Reds out an id with no Dialog row behind it, and says so on hover.</summary>
    private void ShowValidity(string dialogId)
    {
        if (dialogId.Length == 0 || DialogIdCatalog.Contains(dialogId))
        {
            _text.RemoveThemeColorOverride("font_color");
            _text.TooltipText = "";
            return;
        }

        _text.AddThemeColorOverride("font_color", GetThemeColor("error_color", "Editor"));
        _text.TooltipText = $"'{dialogId}' has no row in Dialog_Dialog.txt -- "
                            + "talking to this agent would open nothing.";
    }
}
