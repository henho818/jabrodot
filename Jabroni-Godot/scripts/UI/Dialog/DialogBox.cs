using System;
using System.Collections.Generic;
using Godot;
using Jabroni.Data;

namespace Jabroni.UI.Dialog;

/// <summary>
/// Top-level dialog window: resolves a Dialog's SubDialog lines from the data repositories,
/// cascades their typewriter reveals one after another, and advances/closes on click,
/// mirroring the source project's DialogBox/SubDialogBox cascade-and-click-to-advance model.
/// Also swaps the speaker portrait (see AvatarSheet) whenever a new Dialog is triggered --
/// Dialogs without an AvatarSheet (e.g. the narrator) hide the portrait entirely.
/// Exposes a single Instance (only one dialog box exists) so AI tasks can trigger/observe
/// it without needing a scene-path lookup.
/// </summary>
public partial class DialogBox : Control
{
    [Signal]
    public delegate void ClosedEventHandler();

    public static DialogBox Instance { get; private set; }

    private const string SubDialogLineScenePath = "res://scenes/UI/SubDialogLine.tscn";

    private const float BottomMargin = 32f;
    private const float PortraitGap = 8f;

    /// <summary>
    /// How long the box takes to move vertically -- both the cascade slide onto a new line and
    /// the ease onto a new row within one. 0 makes both snap.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01,or_greater")]
    public float VerticalEaseDuration { get; set; } = 0.25f;

    private VBoxContainer _stack;
    private VBoxContainer _lineContainer;
    private TextureRect _portrait;
    private Texture2D _avatarTexture;
    private PackedScene _lineScene;
    private readonly List<SubDialogLine> _activeLines = new();

    // The stack's top-edge Y, in screen space -- the single value that's smoothly moved rather
    // than snapped whenever the box's height changes. Driven by UpdateTopEdge() each frame, or
    // by SlideStackTopTo()'s tween while a cascade slide owns it.
    private float _displayedTopY;
    private Tween _slideTween;
    private bool _sliding;

    // Ease state for UpdateTopEdge: where the current move started, where it's going, and how
    // far through it is.
    private float _topFromY;
    private float _topTargetY;
    private float _topElapsed;

    public override void _Ready()
    {
        Instance = this;
        _stack = GetNode<VBoxContainer>("Stack");
        _lineContainer = GetNode<VBoxContainer>("Stack/Lines");
        _portrait = GetNode<TextureRect>("Portrait");
        _avatarTexture = GD.Load<Texture2D>(AvatarSheet.TexturePath);
        _lineScene = GD.Load<PackedScene>(SubDialogLineScenePath);
        Visible = false;
    }

    // Width (and thus X-centering) changes every frame while a TextAnimator is mid-reveal, so it
    // is tracked live. Height steps a whole row at a time when a line wraps; rather than the box
    // jumping upward to keep its bottom on the margin, the extra row is allowed to hang below it
    // and UpdateTopEdge eases the top up to reclaim the slack.
    //
    // Portrait is positioned independently, centered on the viewport rather than on the
    // stack's own (currently changing) width -- putting it inside Stack and shrink-centering
    // it against Lines' live width made it visibly drift every frame as a line typed out. The
    // whole stack is always itself centered on the viewport regardless of its width, so
    // "centered on the viewport" and "centered on the final box width" are the same fixed
    // point; anchoring to the viewport just avoids re-deriving that point from a moving target.
    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        Vector2 viewportSize = GetViewportRect().Size;
        Vector2 stackSize = _stack.GetCombinedMinimumSize();
        _stack.Size = stackSize;

        UpdateTopEdge(viewportSize.Y - BottomMargin - stackSize.Y, delta);

        _stack.Position = new Vector2((viewportSize.X - stackSize.X) / 2f, _displayedTopY);

        if (_portrait.Visible)
        {
            Vector2 portraitSize = _portrait.CustomMinimumSize;
            _portrait.Position = new Vector2(
                (viewportSize.X - portraitSize.X) / 2f,
                _displayedTopY - PortraitGap - portraitSize.Y);
        }
    }

    private float TargetTopY()
    {
        return GetViewportRect().Size.Y - BottomMargin - _stack.GetCombinedMinimumSize().Y;
    }

    /// <summary>
    /// Eases the top edge onto a new resting place. Runs every frame but only animates when the
    /// stack's height actually changed -- a line wrapping onto a new row. That's occasional
    /// enough to afford easing in as well as out, unlike the width, which is re-targeted with
    /// almost every character and so has to stay a pure ease-out (see TextAnimator).
    /// </summary>
    private void UpdateTopEdge(float targetTopY, double delta)
    {
        // A cascade slide owns _displayedTopY while it runs. Stay synced to it so that handing
        // back doesn't look like a fresh change and re-animate what the tween just finished.
        if (_sliding)
        {
            SnapTopEdgeState(targetTopY);
            return;
        }

        if (!Mathf.IsEqualApprox(targetTopY, _topTargetY))
        {
            _topFromY = _displayedTopY;
            _topTargetY = targetTopY;
            _topElapsed = 0f;
        }

        if (VerticalEaseDuration <= 0f)
        {
            _displayedTopY = targetTopY;
            return;
        }

        _topElapsed = Mathf.Min(_topElapsed + (float)delta, VerticalEaseDuration);

        float progress = _topElapsed / VerticalEaseDuration;
        float eased = progress * progress * (3f - (2f * progress)); // smoothstep: ease in and out

        _displayedTopY = Mathf.Lerp(_topFromY, _topTargetY, eased);
    }

    /// <summary>Marks the ease as already finished at the given position, so nothing animates from it.</summary>
    private void SnapTopEdgeState(float topY)
    {
        _topFromY = _displayedTopY;
        _topTargetY = topY;
        _topElapsed = VerticalEaseDuration;
    }

    private void SnapStackTop()
    {
        _slideTween?.Kill();
        _sliding = false;
        _displayedTopY = TargetTopY();
        SnapTopEdgeState(_displayedTopY);
    }

    /// <summary>Slides the box (and portrait) up to fit a newly-reserved line, then invokes onComplete.</summary>
    private void SlideStackTopTo(Action onComplete)
    {
        _slideTween?.Kill();
        _sliding = true;
        _slideTween = CreateTween();
        _slideTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _slideTween.TweenMethod(Callable.From<float>(y => _displayedTopY = y), _displayedTopY, TargetTopY(), VerticalEaseDuration);
        _slideTween.TweenCallback(Callable.From(() =>
        {
            // Hand _displayedTopY back to _Process before typing starts, so the line can grow the
            // box upward as it wraps.
            _sliding = false;
            onComplete();
        }));
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

        if (cellRect == null)
        {
            _portrait.Visible = false;
            return;
        }

        _portrait.Texture = new AtlasTexture { Atlas = _avatarTexture, Region = cellRect.Value };
        _portrait.Visible = true;
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

            var line = _lineScene.Instantiate<SubDialogLine>();
            _lineContainer.AddChild(line);
            line.Setup(localizedText, bg, textColor, next, pitchScale);
            line.Visible = false;
            line.AdvanceRequested += () => OnLineAdvanceRequested(line);

            _activeLines.Add(line);
        }
    }

    // The box's very first line (a fresh dialog opening, or one swapped in via a Next
    // transition) snaps into place -- there's no prior box to slide from. Every later line in
    // the same cascade reserves its row height and waits for the resulting slide to finish
    // before typing starts, so growth and typing read as two distinct, sequenced beats rather
    // than happening on top of each other.
    private void StartCascadeFrom(int index, bool slide = true)
    {
        if (index >= _activeLines.Count)
        {
            return;
        }

        var line = _activeLines[index];
        line.Visible = true;

        if (slide)
        {
            SlideStackTopTo(() => line.PlayTyping(() => StartCascadeFrom(index + 1)));
        }
        else
        {
            SnapStackTop();
            line.PlayTyping(() => StartCascadeFrom(index + 1));
        }
    }

    private void OnLineAdvanceRequested(SubDialogLine line)
    {
        string next = line.NextDialogId;
        if (next == DialogSchema.EndCommand)
        {
            Close();
        }
        else if (!string.IsNullOrEmpty(next))
        {
            TriggerDialog(next);
        }
    }

    /// <summary>Force-closes the dialog from outside (e.g. the chat partner walking out of range), same as clicking through to an `&lt;end&gt;` line.</summary>
    public void Close()
    {
        Visible = false;
        ClearLines();
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
