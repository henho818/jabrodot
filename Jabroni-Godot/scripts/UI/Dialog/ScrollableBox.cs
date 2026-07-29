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
    /// How far above the box the invisible touch area reaches. It always spans the full viewport
    /// width and runs to the bottom of the screen, so the misses this is really for -- a thumb
    /// landing beside a narrow line, or in the margin below the box -- are covered regardless.
    /// </summary>
    [Export(PropertyHint.Range, "0,200,1,or_greater")]
    public float TouchAreaMargin { get; set; } = 24f;

    /// <summary>Tints the touch area so its extent can be seen while tuning it. Off in play.</summary>
    [Export]
    public bool ShowTouchArea { get; set; }

    private ScrollContainer _mask;
    private DialogBox _dialogBox;
    private TextureRect _portrait;
    private Control _touchArea;

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
        _touchArea = GetNode<Control>("TouchArea");

        _dialogBox.Host = this;
        _dialogBox.PortraitChanged += OnPortraitChanged;

        _portrait.Visible = false;
        _touchArea.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!_dialogBox.Visible)
        {
            _portrait.Visible = false;
            _touchArea.Visible = false;
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

        // The mask is only as big as the text, which is a small target for a thumb -- and a press
        // that lands beside it, or in the margin below it, reaches ClickToMove and walks the
        // avatar off instead of scrolling. This is the catcher for those: it takes the press so
        // nothing downstream sees it, and it is what the drag handler tests against, so a swipe
        // starting next to the box scrolls it as readily as one starting on the text.
        _touchArea.Visible = true;
        _touchArea.Position = new Vector2(0f, Mathf.Max(0f, topY - TouchAreaMargin));
        _touchArea.Size = new Vector2(viewportSize.X, viewportSize.Y - _touchArea.Position.Y);

        if (ShowTouchArea)
        {
            QueueRedraw();
        }

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

        if (_portrait.Visible)
        {
            Vector2 portraitSize = _portrait.CustomMinimumSize;
            _portrait.Position = new Vector2(
                (viewportSize.X - portraitSize.X) / 2f,
                topY - PortraitGap - portraitSize.Y);
        }
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
            SwallowInsideTouchArea(touch.Position);
            return;
        }

        if (@event is InputEventScreenDrag screenDrag)
        {
            SwallowInsideTouchArea(screenDrag.Position);
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
            // Left for ScrollContainer to actually scroll -- this only notes that the player has
            // taken over, so the pin stops fighting the wheel.
            if (IsInsideTouchArea(button.Position))
            {
                _userScrolled = true;
            }

            return;
        }

        if (button.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (button.Pressed)
        {
            // Nothing to drag when the content already fits, so every press is a tap.
            _dragCandidate = _maxScroll > 0f && IsInsideTouchArea(button.Position);
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

    private void SwallowInsideTouchArea(Vector2 position)
    {
        if (IsInsideTouchArea(position))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Whether a viewport-space pointer position is over the touch area. Goes through the canvas
    /// transform rather than comparing against GetGlobalRect() directly, so it still holds if the
    /// UI's CanvasLayer is ever offset or scaled.
    /// </summary>
    private bool IsInsideTouchArea(Vector2 viewportPosition)
    {
        Vector2 local = _touchArea.GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
        return new Rect2(Vector2.Zero, _touchArea.Size).HasPoint(local);
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
    /// </summary>
    public void SettleForNewLine(Action onComplete)
    {
        _settleTween?.Kill();
        _settling = true;
        _settleTween = CreateTween();
        _settleTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _settleTween.TweenMethod(
            Callable.From<float>(bottom => _displayedBottom = bottom),
            _displayedBottom,
            _dialogBox.ContentSize.Y,
            VerticalEaseDuration);
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

    /// <summary>
    /// Paints the touch area when <see cref="ShowTouchArea"/> is on, so its extent can be seen
    /// while tuning the margin. Drawn from here rather than by the TouchArea node itself, which
    /// is a plain scriptless Control -- this node is anchored to the whole viewport, so its
    /// child's Position/Size are already the rect to paint.
    /// </summary>
    public override void _Draw()
    {
        if (ShowTouchArea && _touchArea != null && _touchArea.Visible)
        {
            DrawRect(new Rect2(_touchArea.Position, _touchArea.Size), new Color(0f, 0.6f, 1f, 0.12f));
        }
    }
}
