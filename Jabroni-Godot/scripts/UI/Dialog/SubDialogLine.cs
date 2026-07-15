using System;
using Godot;

namespace Jabroni.UI.Dialog;

/// <summary>One clickable dialog line/choice: styled background, typewriter text, click to advance or skip.</summary>
public partial class SubDialogLine : PanelContainer
{
    [Signal]
    public delegate void AdvanceRequestedEventHandler();

    private TextAnimator _textAnimator;
    private string _fullText;

    public string NextDialogId { get; private set; }

    public override void _Ready()
    {
        _textAnimator = GetNode<TextAnimator>("Margin/Text");
    }

    public void Setup(string text, Color background, Color textColor, string nextDialogId, float typingPitchScale)
    {
        _fullText = text;
        NextDialogId = nextDialogId;

        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = background });
        _textAnimator.AddThemeColorOverride("default_color", textColor);
        _textAnimator.SetPitch(typingPitchScale);
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
            _textAnimator.SkipToEnd();
        }
        else
        {
            EmitSignal(SignalName.AdvanceRequested);
        }
    }
}
