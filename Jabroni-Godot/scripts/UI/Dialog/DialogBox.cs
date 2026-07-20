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

    private const string EndCommand = "<end>";
    private const string SubDialogLineScenePath = "res://scenes/UI/SubDialogLine.tscn";

    private static readonly string[] SubDialogSlotColumns =
    {
        "SubDialogID0", "SubDialogID1", "SubDialogID2", "SubDialogID3", "SubDialogID4", "SubDialogID5"
    };

    private const float BottomMargin = 32f;
    private const float PortraitGap = 8f;
    private const float SlideDuration = 0.25f;

    private VBoxContainer _stack;
    private VBoxContainer _lineContainer;
    private TextureRect _portrait;
    private Texture2D _avatarTexture;
    private PackedScene _lineScene;
    private readonly List<SubDialogLine> _activeLines = new();

    // The stack's top-edge Y, in screen space -- the single value that's smoothly slid rather
    // than snapped whenever a new line's height gets reserved. Owned/updated by
    // SnapStackTop()/SlideStackTopTo(), read every frame in _Process.
    private float _displayedTopY;
    private Tween _slideTween;

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

    // Width (and thus X-centering) changes every frame while a TextAnimator is mid-reveal, so
    // that part is still recomputed live here. Height/Y is different: it only changes at the
    // discrete moment a new line's row gets reserved, so it's driven by _displayedTopY (see
    // SnapStackTop/SlideStackTopTo) instead of being recomputed from scratch every frame.
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

    private void SnapStackTop()
    {
        _slideTween?.Kill();
        _displayedTopY = TargetTopY();
    }

    /// <summary>Slides the box (and portrait) up to fit a newly-reserved line, then invokes onComplete.</summary>
    private void SlideStackTopTo(Action onComplete)
    {
        _slideTween?.Kill();
        _slideTween = CreateTween();
        _slideTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _slideTween.TweenMethod(Callable.From<float>(y => _displayedTopY = y), _displayedTopY, TargetTopY(), SlideDuration);
        _slideTween.TweenCallback(Callable.From(onComplete));
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
        string sheet = dialogRow.GetString("AvatarSheet");
        int avatarIndex = dialogRow.GetInt("AvatarIndex", -1);
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

        foreach (string column in SubDialogSlotColumns)
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

            var styleRow = styleRepo.Get(subDialogRow.GetString("Style"));
            Color bg = styleRow != null ? styleRow.GetColor("BgColor") : Colors.White;
            Color textColor = styleRow != null ? styleRow.GetColor("TextColor") : Colors.Black;
            string localizedText = Tr(subDialogRow.GetString("LocalizationDialogID"));
            string next = subDialogRow.GetString("Next");

            // Pitch is per-line authored data (source project fed it into a pitch-shifter mixer
            // effect of unknown units). Treated here as semitones -- 0 = unshifted -- and
            // converted to Godot's linear AudioStreamPlayer.PitchScale.
            float pitchSemitones = subDialogRow.GetFloat("Pitch", 0f);
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
        if (next == EndCommand)
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
