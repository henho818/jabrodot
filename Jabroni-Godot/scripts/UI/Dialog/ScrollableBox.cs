using System;
using Godot;

namespace Jabroni.UI.Dialog;

/// <summary>
/// Outer frame for the dialog UI: owns where the box sits on screen (bottom-anchored,
/// horizontally centred), caps how tall it is allowed to grow, and masks + scrolls the
/// <see cref="DialogBox"/> inside it once the content outgrows that cap. Also carries the
/// speaker portrait, which sits above the mask rather than inside it so it never scrolls away.
/// <para>
/// The whole layout is driven from one eased value, <see cref="_displayedBottom"/>: how far down
/// the content, measured from its top, is currently shown at the viewport's bottom edge. Below
/// the cap that value is the box's height and the box grows upward; at the cap the height pins
/// and the same value becomes a scroll offset instead. One ease therefore covers growing,
/// scrolling, and the frame that crosses between them, with no seam where the behaviour swaps.
/// </para>
/// </summary>
public partial class ScrollableBox : Control
{
    private const float BottomMargin = 32f;
    private const float PortraitGap = 8f;

    /// <summary>
    /// Slack added either side of the mask. TextAnimator deliberately lets a line overhang its
    /// panel while the eased width catches up to the revealed text (see its ClipContents note);
    /// without this margin the mask would cut that overhang off mid-glyph, which is exactly what
    /// leaving the label unclipped was meant to avoid.
    /// </summary>
    private const float MaskHorizontalSlack = 16f;

    /// <summary>Tallest the box may get, as a fraction of viewport height. Past this it scrolls.</summary>
    [Export(PropertyHint.Range, "0.05,1,0.01")]
    public float MaxHeightFraction { get; set; } = 1f / 3f;

    /// <summary>
    /// How long the box takes to move vertically -- the cascade slide onto a new line, the ease
    /// onto a new row within one, and (once capped) the scroll that stands in for both. 0 makes
    /// them all snap.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01,or_greater")]
    public float VerticalEaseDuration { get; set; } = 0.25f;

    /// <summary>
    /// How far a press has to travel vertically before it counts as a drag rather than a tap.
    /// The whole budget for a finger that wobbles on the way down -- too small and taps start
    /// scrolling the box instead of picking the line under them.
    /// </summary>
    [Export(PropertyHint.Range, "0,64,1,or_greater")]
    public float DragDeadzone { get; set; } = 8f;

    /// <summary>
    /// How far past the dialog's own extent the backdrop reaches. It always spans the full viewport
    /// width and runs to the bottom of the screen, so the misses this is really for -- a thumb
    /// landing beside a narrow line, or in the margin below the box -- are covered regardless.
    /// </summary>
    [Export(PropertyHint.Range, "0,200,1,or_greater")]
    public float BackdropMargin { get; set; } = 24f;

    /// <summary>
    /// Tint painted over the world behind the dialog. Its rect is exactly the region that swallows
    /// pointer input, so darkening it is what tells the player where clicks stop reaching the world
    /// -- keep it visible enough to read as a boundary.
    /// </summary>
    [Export]
    public Color BackdropColor { get; set; } = new(0f, 0f, 0f, 0.45f);

    /// <summary>How far one wheel notch scrolls the box, in pixels.</summary>
    [Export(PropertyHint.Range, "1,200,1,or_greater")]
    public float WheelScrollStep { get; set; } = 48f;

    private ScrollContainer _mask;
    private DialogBox _dialogBox;
    private TextureRect _portrait;
    private ColorRect _backdrop;

    // Drag-to-scroll state. _dragCandidate spans the whole press; _dragging only turns on once
    // the deadzone is beaten, and is what decides whether the release is a tap or a drag's end.
    private bool _dragCandidate;
    private bool _dragging;
    private Vector2 _pressPosition;
    private float _lastDragY;

    // Scroll offset accumulated in float, so a slow drag isn't lost to rounding a sub-pixel
    // delta to zero every event.
    private float _dragScroll;

    /// <summary>How far there is left to scroll, refreshed each frame for the drag handler.</summary>
    private float _maxScroll;

    /// <summary>
    /// Set once the player takes the scrollbar over by hand. Suspends the follow-the-bottom pin
    /// -- otherwise a line typing away underneath would drag the view straight back down --
    /// until they scroll back to the bottom themselves, or a new dialog resets the box.
    /// </summary>
    private bool _userScrolled;

    // The eased "content bottom" described in the class summary, plus the ease's own state:
    // where the current move started, where it is going, and how far through it is.
    private float _displayedBottom;
    private float _fromBottom;
    private float _targetBottom;
    private float _elapsed;

    private Tween _settleTween;
    private bool _settling;

    // Last frame's view, so _Process can tell a view that is still moving (we pin it to the
    // bottom so the typing stays visible) from one at rest (the player owns the scrollbar).
    private float _lastDisplayedBottom;
    private float _lastContentHeight;

    public override void _Ready()
    {
        _mask = GetNode<ScrollContainer>("Mask");
        _dialogBox = GetNode<DialogBox>("Mask/DialogBox");
        _portrait = GetNode<TextureRect>("Portrait");
        _backdrop = GetNode<ColorRect>("Backdrop");

        _dialogBox.Host = this;
        _dialogBox.PortraitChanged += OnPortraitChanged;

        _portrait.Visible = false;
        _backdrop.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!_dialogBox.Visible)
        {
            _portrait.Visible = false;
            _backdrop.Visible = false;
            return;
        }

        Vector2 viewportSize = GetViewportRect().Size;
        Vector2 contentSize = _dialogBox.ContentSize;
        float maxHeight = viewportSize.Y * MaxHeightFraction;

        UpdateBottom(contentSize.Y, delta);

        float height = Mathf.Min(_displayedBottom, maxHeight);
        float width = MaskWidth(contentSize.X, contentSize.Y > maxHeight, viewportSize.X);
        float topY = viewportSize.Y - BottomMargin - height;

        _mask.Position = new Vector2((viewportSize.X - width) / 2f, topY);
        _mask.Size = new Vector2(width, height);

        // Placed before the backdrop is sized, because the backdrop has to reach up over it. The
        // portrait is mouse_filter IGNORE and sits a good way above the box (PortraitGap plus its
        // own height), so any part of it the backdrop doesn't reach is a hole a click drops
        // straight through to ClickToMove -- which is what made clicking a speaker's face walk the
        // avatar off mid-conversation.
        float dialogTop = topY;

        if (_portrait.Visible)
        {
            Vector2 portraitSize = _portrait.CustomMinimumSize;
            _portrait.Position = new Vector2(
                (viewportSize.X - portraitSize.X) / 2f,
                topY - PortraitGap - portraitSize.Y);

            dialogTop = _portrait.Position.Y;
        }

        // The mask is only as big as the text, which is a small target for a thumb -- and a press
        // that lands beside it, or in the margin below it, reaches ClickToMove and walks the
        // avatar off instead of scrolling. This is the catcher for those: MOUSE_FILTER_STOP, so it
        // takes the press and nothing downstream sees it, and it is what the drag handler tests
        // against, so a swipe starting next to the box scrolls it as readily as one starting on
        // the text. Painting it is the other half of the job -- the darkened band is the player's
        // only cue for where clicks stop reaching the world, so its rect and the blocked region
        // are deliberately the same rect rather than two that have to be kept in agreement.
        _backdrop.Visible = true;
        _backdrop.Color = BackdropColor;
        _backdrop.Position = new Vector2(0f, Mathf.Max(0f, dialogTop - BackdropMargin));
        _backdrop.Size = new Vector2(viewportSize.X, viewportSize.Y - _backdrop.Position.Y);

        _maxScroll = Mathf.Max(0f, contentSize.Y - height);

        // Scrolling back to the bottom by hand hands the pin back, the way a chat log re-follows
        // once you return to the newest message.
        if (_userScrolled && _mask.ScrollVertical >= _maxScroll - 1f)
        {
            _userScrolled = false;
        }

        // Pin to the bottom only while the view is actually moving -- growing, easing, or
        // typing a line. Once it comes to rest the scrollbar is left alone, so the player can
        // read back up through a long dialog without it yanking itself back down every frame.
        bool viewMoving = !Mathf.IsEqualApprox(_displayedBottom, _lastDisplayedBottom)
            || !Mathf.IsEqualApprox(contentSize.Y, _lastContentHeight);

        if (viewMoving && !_userScrolled)
        {
            _mask.ScrollVertical = Mathf.RoundToInt(Mathf.Max(0f, _displayedBottom - height));
        }

        _lastDisplayedBottom = _displayedBottom;
        _lastContentHeight = contentSize.Y;
    }

    // Drag-to-scroll has to be read here rather than in _GuiInput, because by the time GUI input
    // is dispatched a SubDialogLine (mouse_filter Stop) has already swallowed the press for
    // itself and no ancestor sees it. _Input runs ahead of that dispatch, so the drag can be
    // recognised without taking the press away from the line underneath it.
    //
    // What keeps taps working is that a line only acts on the *release*: a press is left to
    // travel down the tree untouched, and only once the deadzone is beaten does this start
    // eating events -- including the release that ends the drag, so the line under the finger
    // doesn't read the end of a scroll as a choice. A press that never travels that far is
    // never touched at all, and taps through exactly as before.
    public override void _Input(InputEvent @event)
    {
        if (!_dialogBox.Visible)
        {
            return;
        }

        // Real touch events are swallowed inside the mask so ScrollContainer's own touch panning
        // can't scroll the box a second time. The drag itself runs off the mouse events Godot
        // emulates from that same touch (input_devices/pointing/emulate_mouse_from_touch, on by
        // default), so finger and pointer share one code path instead of two that must agree.
        if (@event is InputEventScreenTouch touch)
        {
            SwallowInsideBackdrop(touch.Position);
            return;
        }

        if (@event is InputEventScreenDrag screenDrag)
        {
            SwallowInsideBackdrop(screenDrag.Position);
            return;
        }

        if (@event is InputEventMouseButton button)
        {
            HandleButton(button);
            return;
        }

        if (@event is InputEventMouseMotion motion && _dragCandidate)
        {
            HandleDragMotion(motion);
        }
    }

    private void HandleButton(InputEventMouseButton button)
    {
        if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            HandleWheel(button);
            return;
        }

        if (button.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (button.Pressed)
        {
            // Nothing to drag when the content already fits, so every press is a tap.
            _dragCandidate = _maxScroll > 0f && IsInsideBackdrop(button.Position);
            _dragging = false;
            _pressPosition = button.Position;
            _lastDragY = button.Position.Y;
            _dragScroll = _mask.ScrollVertical;
            return;
        }

        bool wasDragging = _dragging;
        _dragCandidate = false;
        _dragging = false;

        if (wasDragging)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Scrolls the box on the wheel and takes the event with it. Both halves matter. Doing the
    /// scroll here instead of leaving it to the ScrollContainer means the whole touch area
    /// answers the wheel, not just the narrow strip of text -- the same reason the drag handler
    /// tests against that area. Marking it handled is what stops the notch travelling on to
    /// OrbitCamera and zooming the world out behind the dialog on the same flick.
    /// <para>
    /// Being over the backdrop is the whole test -- a box with nothing left to scroll still claims
    /// the notch and does nothing with it. The rule the player learns is one rule, "the darkened
    /// region is the dialog's, not the world's", and it has to hold for every pointer input alike
    /// or it isn't a rule they can rely on. Letting a short dialog pass the wheel through would
    /// make the world zoom under the cursor depending on how many lines happened to be on screen,
    /// which is exactly the kind of state-dependent input this backdrop exists to get rid of.
    /// </para>
    /// </summary>
    private void HandleWheel(InputEventMouseButton button)
    {
        if (!IsInsideBackdrop(button.Position))
        {
            return;
        }

        if (button.Pressed && _maxScroll > 0f)
        {
            // Factor is how far a high-resolution wheel or trackpad actually turned; an ordinary
            // notch reports 1.
            float notches = button.Factor > 0f ? button.Factor : 1f;
            float direction = button.ButtonIndex == MouseButton.WheelUp ? -1f : 1f;

            _dragScroll = Mathf.Clamp(
                _mask.ScrollVertical + (direction * notches * WheelScrollStep),
                0f,
                _maxScroll);
            _mask.ScrollVertical = Mathf.RoundToInt(_dragScroll);

            // As with a drag: taking the scrollbar by hand suspends the follow-the-bottom pin
            // until the player returns to the bottom themselves.
            _userScrolled = true;
        }

        // Godot sends a press and a release for every notch. The release is swallowed too, so
        // nothing downstream ever sees half a notch and tries to act on it.
        GetViewport().SetInputAsHandled();
    }

    private void HandleDragMotion(InputEventMouseMotion motion)
    {
        float y = motion.Position.Y;

        if (!_dragging)
        {
            // Only the Y travel counts, so a horizontal swipe across the lines stays a tap.
            if (Mathf.Abs(y - _pressPosition.Y) <= DragDeadzone)
            {
                return;
            }

            // Measure from here, not from the press, so crossing the deadzone doesn't start the
            // drag with a jump of the deadzone's own width.
            _dragging = true;
            _lastDragY = y;
        }

        _userScrolled = true;
        _dragScroll = Mathf.Clamp(_dragScroll - (y - _lastDragY), 0f, _maxScroll);
        _lastDragY = y;

        _mask.ScrollVertical = Mathf.RoundToInt(_dragScroll);
        GetViewport().SetInputAsHandled();
    }

    private void SwallowInsideBackdrop(Vector2 position)
    {
        if (IsInsideBackdrop(position))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Whether a viewport-space pointer position lands on the dialog -- i.e. on the darkened
    /// backdrop, which the player is being shown as the region that belongs to the dialog rather
    /// than to the world. False whenever no dialog is up.
    /// <para>
    /// Public so <c>ClickToMove</c> can refuse such a point outright. The backdrop being
    /// MOUSE_FILTER_STOP already stops a press reaching unhandled input, so that check is a second
    /// line of defence rather than the mechanism -- it costs a rect test and makes the block hold
    /// for any caller that arrives at a screen point some other way than GUI dispatch.
    /// </para>
    /// </summary>
    public bool BlocksPointer(Vector2 viewportPosition)
        => _backdrop != null && _backdrop.Visible && IsInsideBackdrop(viewportPosition);

    /// <summary>
    /// Whether a viewport-space pointer position is over the backdrop. Goes through the canvas
    /// transform rather than comparing against GetGlobalRect() directly, so it still holds if the
    /// UI's CanvasLayer is ever offset or scaled.
    /// </summary>
    private bool IsInsideBackdrop(Vector2 viewportPosition)
    {
        Vector2 local = _backdrop.GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
        return new Rect2(Vector2.Zero, _backdrop.Size).HasPoint(local);
    }

    /// <summary>
    /// Mask width: the content plus its overhang slack, widened again for the scrollbar when one
    /// is showing so the bar sits beside the text rather than over it, and finally clamped to the
    /// viewport.
    /// </summary>
    private float MaskWidth(float contentWidth, bool scrollable, float viewportWidth)
    {
        float width = contentWidth + MaskHorizontalSlack;

        if (scrollable)
        {
            width += _mask.GetVScrollBar().GetCombinedMinimumSize().X;
        }

        return Mathf.Min(width, viewportWidth);
    }

    /// <summary>
    /// Eases the shown content bottom onto a new resting place. Runs every frame but only
    /// animates when the content height actually changed -- a line wrapping onto a new row.
    /// That's occasional enough to afford easing in as well as out, unlike TextAnimator's width,
    /// which is re-targeted with almost every character and so has to stay a pure ease-out.
    /// </summary>
    private void UpdateBottom(float targetBottom, double delta)
    {
        // A cascade settle owns _displayedBottom while it runs. Stay synced to it so that handing
        // back doesn't look like a fresh change and re-animate what the tween just finished.
        if (_settling)
        {
            SnapBottomState(targetBottom);
            return;
        }

        if (!Mathf.IsEqualApprox(targetBottom, _targetBottom))
        {
            _fromBottom = _displayedBottom;
            _targetBottom = targetBottom;
            _elapsed = 0f;
        }

        if (VerticalEaseDuration <= 0f)
        {
            _displayedBottom = targetBottom;
            return;
        }

        _elapsed = Mathf.Min(_elapsed + (float)delta, VerticalEaseDuration);

        float progress = _elapsed / VerticalEaseDuration;
        float eased = progress * progress * (3f - (2f * progress)); // smoothstep: ease in and out

        _displayedBottom = Mathf.Lerp(_fromBottom, _targetBottom, eased);
    }

    /// <summary>Marks the ease as already finished at the given value, so nothing animates from it.</summary>
    private void SnapBottomState(float bottom)
    {
        _fromBottom = _displayedBottom;
        _targetBottom = bottom;
        _elapsed = VerticalEaseDuration;
    }

    /// <summary>
    /// Drops the box straight onto its current content, no animation -- for a dialog's very first
    /// line, where there is no prior box to move from.
    /// </summary>
    public void SnapToContent()
    {
        _settleTween?.Kill();
        _settling = false;
        _displayedBottom = _dialogBox.ContentSize.Y;
        SnapBottomState(_displayedBottom);

        // A new dialog is a clean slate: whatever the player had scrolled to belonged to the
        // lines that just got cleared out.
        _userScrolled = false;

        // Force the first frame to pin to the bottom rather than read as "at rest".
        _lastDisplayedBottom = float.NaN;
    }

    /// <summary>
    /// Moves the box onto a newly-reserved line -- growing upward while there is room, scrolling
    /// once there isn't -- then invokes onComplete so the line can start typing. Growth and
    /// typing stay two distinct, sequenced beats rather than happening on top of each other.
    /// <para>
    /// <paramref name="duration"/> overrides <see cref="VerticalEaseDuration"/> for this one move.
    /// A fast-revealed cascade passes the same budget it gives the typing, so the box keeps step
    /// with the text instead of holding each line back for a quarter of a second first.
    /// </para>
    /// </summary>
    public void SettleForNewLine(Action onComplete, float? duration = null)
    {
        _settleTween?.Kill();
        _settling = true;
        _settleTween = CreateTween();
        _settleTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _settleTween.TweenMethod(
            Callable.From<float>(bottom => _displayedBottom = bottom),
            _displayedBottom,
            _dialogBox.ContentSize.Y,
            Mathf.Max(0f, duration ?? VerticalEaseDuration));
        _settleTween.TweenCallback(Callable.From(() =>
        {
            // Hand _displayedBottom back to _Process before typing starts, so the line can keep
            // growing the box (or scrolling it) as it wraps.
            _settling = false;
            onComplete();
        }));
    }

    private void OnPortraitChanged(Texture2D texture)
    {
        _portrait.Texture = texture;
        _portrait.Visible = texture != null;
    }

}
