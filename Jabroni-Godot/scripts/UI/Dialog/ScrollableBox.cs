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

    private ScrollContainer _mask;
    private DialogBox _dialogBox;
    private TextureRect _portrait;

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

        _dialogBox.Host = this;
        _dialogBox.PortraitChanged += OnPortraitChanged;

        _portrait.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!_dialogBox.Visible)
        {
            _portrait.Visible = false;
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

        // Pin to the bottom only while the view is actually moving -- growing, easing, or
        // typing a line. Once it comes to rest the scrollbar is left alone, so the player can
        // read back up through a long dialog without it yanking itself back down every frame.
        bool viewMoving = !Mathf.IsEqualApprox(_displayedBottom, _lastDisplayedBottom)
            || !Mathf.IsEqualApprox(contentSize.Y, _lastContentHeight);

        if (viewMoving)
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
}
