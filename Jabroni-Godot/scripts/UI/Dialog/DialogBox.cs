using System.Collections.Generic;
using Godot;
using Jabroni.Data;
using Jabroni.Inventory;

namespace Jabroni.UI.Dialog;

/// <summary>
/// The dialog's content: resolves a Dialog's SubDialog lines from the data repositories,
/// cascades their typewriter reveals one after another, and advances/closes on click,
/// mirroring the source project's DialogBox/SubDialogBox cascade-and-click-to-advance model.
/// Also announces the speaker portrait (see AvatarSheet) whenever a new Dialog is triggered --
/// Dialogs without an AvatarSheet (e.g. the narrator) announce none -- and hands a line's
/// ItemAward to PlayerInventory as the player clicks through it.
/// Exposes a single Instance (only one dialog box exists) so AI tasks can trigger/observe
/// it without needing a scene-path lookup.
/// <para>
/// Where the box sits on screen, how tall it may get, and what happens when the lines outgrow
/// that are all <see cref="ScrollableBox"/>'s -- this class lays its stack out at its own
/// origin and reports how big it is via <see cref="ContentSize"/>. The portrait likewise lives
/// on the host, above the mask, so it doesn't scroll away with the text.
/// </para>
/// </summary>
public partial class DialogBox : Control
{
    [Signal]
    public delegate void ClosedEventHandler();

    /// <summary>Raised on every dialog change with the new speaker's portrait, or null for none.</summary>
    [Signal]
    public delegate void PortraitChangedEventHandler(Texture2D texture);

    public static DialogBox Instance { get; private set; }

    private const string SubDialogLineScenePath = "res://scenes/UI/SubDialogLine.tscn";

    /// <summary>The frame that positions, caps and scrolls this box. Assigned by it in _Ready.</summary>
    public ScrollableBox Host { get; set; }

    private VBoxContainer _stack;
    private VBoxContainer _lineContainer;
    private Texture2D _avatarTexture;
    private PackedScene _lineScene;
    private readonly List<SubDialogLine> _activeLines = new();

    /// <summary>How much room the lines currently want -- what the host sizes and scrolls against.</summary>
    public Vector2 ContentSize => _stack?.GetCombinedMinimumSize() ?? Vector2.Zero;

    public override void _Ready()
    {
        Instance = this;
        _stack = GetNode<VBoxContainer>("Stack");
        _lineContainer = GetNode<VBoxContainer>("Stack/Lines");
        _avatarTexture = GD.Load<Texture2D>(AvatarSheet.TexturePath);
        _lineScene = GD.Load<PackedScene>(SubDialogLineScenePath);
        Visible = false;
    }

    // Width (and thus the stack's own size) changes every frame while a TextAnimator is
    // mid-reveal, so it is tracked live rather than left to a container's layout pass. Height
    // steps a whole row at a time when a line wraps; the host eases onto that new height, or
    // scrolls to it once the box has hit its cap.
    //
    // CustomMinimumSize is what the enclosing ScrollContainer reads to decide how far there is
    // to scroll, so it has to follow the stack every frame too.
    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        Vector2 stackSize = _stack.GetCombinedMinimumSize();
        _stack.Size = stackSize;
        _stack.Position = Vector2.Zero;
        CustomMinimumSize = stackSize;
    }

    public void TriggerDialog(string dialogId)
    {
        var dialogRepo = GetNode<DialogRepository>("/root/DialogRepository");
        var dialogRow = dialogRepo.Get(dialogId);
        if (dialogRow == null)
        {
            GD.PushWarning($"DialogBox: unknown dialog id '{dialogId}'");
            return;
        }

        ClearLines();
        UpdatePortrait(dialogRow);
        BuildLines(dialogRow);

        Visible = true;
        StartCascadeFrom(0, slide: false);
    }

    private void UpdatePortrait(TsvRow dialogRow)
    {
        string sheet = dialogRow.GetString(DialogSchema.AvatarSheetColumn);
        int avatarIndex = dialogRow.GetInt(DialogSchema.AvatarIndexColumn, -1);
        Rect2? cellRect = !string.IsNullOrEmpty(sheet) ? AvatarSheet.GetCellRect(avatarIndex) : null;

        Texture2D texture = cellRect == null
            ? null
            : new AtlasTexture { Atlas = _avatarTexture, Region = cellRect.Value };

        EmitSignal(SignalName.PortraitChanged, texture);
    }

    private void BuildLines(TsvRow dialogRow)
    {
        var subDialogRepo = GetNode<SubDialogRepository>("/root/SubDialogRepository");
        var styleRepo = GetNode<SubDialogStyleRepository>("/root/SubDialogStyleRepository");

        foreach (string column in DialogSchema.SubDialogSlotColumns)
        {
            string subDialogId = dialogRow.GetString(column);
            if (string.IsNullOrEmpty(subDialogId))
            {
                continue;
            }

            var subDialogRow = subDialogRepo.Get(subDialogId);
            if (subDialogRow == null)
            {
                GD.PushWarning($"DialogBox: unknown sub-dialog id '{subDialogId}'");
                continue;
            }

            var styleRow = styleRepo.Get(subDialogRow.GetString(DialogSchema.StyleColumn));
            Color bg = styleRow != null ? styleRow.GetColor("BgColor") : Colors.White;
            Color textColor = styleRow != null ? styleRow.GetColor("TextColor") : Colors.Black;
            string localizedText = Tr(subDialogRow.GetString(DialogSchema.LocalizationIdColumn));
            string next = subDialogRow.GetString(DialogSchema.NextColumn);

            // Pitch is per-line authored data (source project fed it into a pitch-shifter mixer
            // effect of unknown units). Treated here as semitones -- 0 = unshifted -- and
            // converted to Godot's linear AudioStreamPlayer.PitchScale.
            float pitchSemitones = subDialogRow.GetFloat(DialogSchema.PitchColumn, 0f);
            float pitchScale = Mathf.Pow(2f, pitchSemitones / 12f);

            string itemAward = subDialogRow.GetString(DialogSchema.ItemAwardColumn);

            var line = _lineScene.Instantiate<SubDialogLine>();
            _lineContainer.AddChild(line);
            line.Setup(localizedText, bg, textColor, next, itemAward, pitchScale);
            line.Visible = false;
            line.AdvanceRequested += () => OnLineAdvanceRequested(line);

            _activeLines.Add(line);
        }
    }

    // The box's very first line (a fresh dialog opening, or one swapped in via a Next
    // transition) snaps into place -- there's no prior box to move from. Every later line in
    // the same cascade reserves its row height and waits for the host's resulting settle --
    // growing the box, or scrolling it once capped -- to finish before typing starts.
    private void StartCascadeFrom(int index, bool slide = true)
    {
        if (index >= _activeLines.Count)
        {
            return;
        }

        var line = _activeLines[index];
        line.Visible = true;

        if (slide && Host != null)
        {
            Host.SettleForNewLine(() => line.PlayTyping(() => StartCascadeFrom(index + 1)));
        }
        else
        {
            Host?.SnapToContent();
            line.PlayTyping(() => StartCascadeFrom(index + 1));
        }
    }

    private void OnLineAdvanceRequested(SubDialogLine line)
    {
        string next = line.NextDialogId;

        // A line with no Next is a statement, not a choice: clicking it leaves the box exactly as
        // it is. Bailing out first is what keeps an ItemAward on such a line from being handed
        // over again on every further click -- there is no single moment to grant it, so it isn't
        // granted at all, and DialogGraphValidator reports the line rather than letting it look
        // like it works.
        if (string.IsNullOrEmpty(next))
        {
            return;
        }

        // Granted before the transition, while this line is still the one that was clicked. Both
        // branches below clear the box, which detaches the line immediately (see ClearLines), so
        // a second click can't reach it and award twice.
        GrantAward(line.ItemAwardId);

        if (next == DialogSchema.EndCommand)
        {
            Close();
        }
        else
        {
            TriggerDialog(next);
        }
    }

    /// <summary>
    /// Hands a line's ItemAward to the player. PlayerInventory already warns about an id the item
    /// table doesn't have, so a typo'd award is reported rather than silently dropped.
    /// </summary>
    private void GrantAward(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            return;
        }

        GetNode<PlayerInventory>("/root/PlayerInventory").Add(itemId);
    }

    /// <summary>Force-closes the dialog from outside (e.g. the chat partner walking out of range), same as clicking through to an `&lt;end&gt;` line.</summary>
    public void Close()
    {
        Visible = false;
        ClearLines();
        EmitSignal(SignalName.PortraitChanged, (Texture2D)null);
        EmitSignal(SignalName.Closed);
    }

    private void ClearLines()
    {
        // QueueFree() alone defers removal to end-of-frame, so a line stays in _lineContainer
        // (still Visible, still counted by GetCombinedMinimumSize()) for the rest of this frame
        // -- which the very next TriggerDialog call measures against, inflating the box height
        // and throwing off its Y position for one dialog transition. RemoveChild() first makes
        // the detach immediate.
        foreach (var line in _activeLines)
        {
            _lineContainer.RemoveChild(line);
            line.QueueFree();
        }

        _activeLines.Clear();
    }
}
