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

    /// <summary>
    /// Set for the rest of the current cascade once the player clicks through a line that is still
    /// typing, and cleared by the next dialog. See <see cref="OnLineSkipRequested"/>.
    /// </summary>
    private bool _fastReveal;

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

        // A new dialog is new text to read, so it starts at reading pace again -- the previous
        // one's hurry-up shouldn't carry over and flash its first line past the player.
        _fastReveal = false;

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
            line.SkipRequested += () => OnLineSkipRequested(line);

            _activeLines.Add(line);
        }
    }

    // The box's very first line (a fresh dialog opening, or one swapped in via a Next
    // transition) snaps into place -- there's no prior box to move from. Every later line in
    // the same cascade reserves its row height and waits for the host's resulting settle --
    // growing the box, or scrolling it once capped -- to finish before typing starts.
    //
    // Once _fastReveal is on, both of those beats shrink to one character's worth of time apiece:
    // the settle is given the same budget as the typing, since leaving it at its usual quarter of
    // a second would have the box, not the text, deciding how long the player waits. The cascade
    // is still a cascade -- lines arrive one after another, just several times a second.
    private void StartCascadeFrom(int index, bool slide = true)
    {
        if (index >= _activeLines.Count)
        {
            return;
        }

        var line = _activeLines[index];
        line.Visible = true;
        line.FastReveal = _fastReveal;

        if (slide && Host != null)
        {
            float? settleDuration = _fastReveal ? TextAnimator.CharacterDuration : null;
            Host.SettleForNewLine(() => line.PlayTyping(() => StartCascadeFrom(index + 1)), settleDuration);
        }
        else
        {
            Host?.SnapToContent();
            line.PlayTyping(() => StartCascadeFrom(index + 1));
        }
    }

    /// <summary>
    /// Handles a click on a line that is still typing: finishes that line at once, as it always
    /// has, and puts every line still to come into fast reveal.
    /// <para>
    /// The click is read as "stop making me wait", so it applies to the whole rest of the cascade
    /// rather than just the line under the cursor. Waiting is worse here than it looks: the lines
    /// that follow a statement are usually the choices, and clicking one of those to hurry it
    /// along picks it instead -- so without this the player has no way to skip ahead that doesn't
    /// also answer for them.
    /// </para>
    /// </summary>
    private void OnLineSkipRequested(SubDialogLine line)
    {
        // Before the skip, not after: finishing the line runs the cascade straight on to the next
        // one, which reads _fastReveal as it starts.
        _fastReveal = true;
        line.SkipTyping();
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
