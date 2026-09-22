using System;
using Godot;

namespace Jabroni.UI.Dialog;

/// <summary>One clickable dialog line/choice: styled background, typewriter text, click to advance or skip.</summary>
public partial class SubDialogLine : PanelContainer
{
    [Signal]
    public delegate void AdvanceRequestedEventHandler();

    /// <summary>
    /// Raised instead of <see cref="AdvanceRequestedEventHandler"/> when the click lands on a line
    /// that is still typing. The skip itself is left to DialogBox rather than done here, so the
    /// rest of the cascade can be put into fast reveal first -- finishing this line restarts the
    /// cascade immediately, and by then the mode has to already be set.
    /// </summary>
    [Signal]
    public delegate void SkipRequestedEventHandler();

    private TextAnimator _textAnimator;
    private string _fullText;

    public string NextDialogId { get; private set; }

    /// <summary>Item id this line hands over when it is clicked through, or empty for none.</summary>
    public string ItemAwardId { get; private set; }

    public override void _Ready()
    {
        _textAnimator = GetNode<TextAnimator>("Margin/Text");
    }

    public void Setup(
        string text,
        Color background,
        Color textColor,
        string nextDialogId,
        string itemAwardId,
        float typingPitchScale)
    {
        _fullText = text;
        NextDialogId = nextDialogId;
        ItemAwardId = itemAwardId;

        // Rounded corners + soft shadow stand in for the source project's 9-sliced
        // Background.png sprite (not ported as a texture -- StyleBoxFlat gets the same look
        // without needing to import/9-patch a raster asset).
        var styleBox = new StyleBoxFlat
        {
            BgColor = background,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ShadowColor = new Color(0f, 0f, 0f, 0.25f),
            ShadowSize = 6,
        };
        AddThemeStyleboxOverride("panel", styleBox);
        _textAnimator.AddThemeColorOverride("default_color", textColor);
        _textAnimator.SetPitch(typingPitchScale);
    }

    /// <summary>Whether this line reveals a character at a time or all at once -- see <see cref="TextAnimator.FastReveal"/>.</summary>
    public bool FastReveal
    {
        get => _textAnimator.FastReveal;
        set => _textAnimator.FastReveal = value;
    }

    /// <summary>Drops the rest of this line on screen at once, completing its reveal.</summary>
    public void SkipTyping()
    {
        _textAnimator.SkipToEnd();
    }

    /// <summary>Starts the typewriter reveal, invoking onComplete once it finishes (or is skipped).</summary>
    public void PlayTyping(Action onComplete)
    {
        void Handler()
        {
            _textAnimator.AnimationCompleted -= Handler;
            onComplete?.Invoke();
        }

        _textAnimator.AnimationCompleted += Handler;
        _textAnimator.Play(_fullText);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            return;
        }

        if (_textAnimator.IsAnimating)
        {
            EmitSignal(SignalName.SkipRequested);
        }
        else
        {
            EmitSignal(SignalName.AdvanceRequested);
        }
    }
}
