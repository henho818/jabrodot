using Godot;

namespace Jabroni.UI.Dialog;

/// <summary>Typewriter reveal for a RichTextLabel: advances VisibleCharacters over time.</summary>
public partial class TextAnimator : RichTextLabel
{
    [Signal]
    public delegate void AnimationCompletedEventHandler();

    private const float CharsPerSecond = 16.7f;

    private float _charsRevealed;
    private int _totalChars;
    private bool _isAnimating;

    public bool IsAnimating => _isAnimating;

    public void Play(string fullText)
    {
        Text = fullText;
        VisibleCharacters = 0;
        _totalChars = GetTotalCharacterCount();
        _charsRevealed = 0f;
        _isAnimating = _totalChars > 0;

        if (!_isAnimating)
        {
            EmitSignal(SignalName.AnimationCompleted);
        }
    }

    public void SkipToEnd()
    {
        if (!_isAnimating)
        {
            return;
        }

        VisibleCharacters = -1;
        _isAnimating = false;
        EmitSignal(SignalName.AnimationCompleted);
    }

    public override void _Process(double delta)
    {
        if (!_isAnimating)
        {
            return;
        }

        _charsRevealed += CharsPerSecond * (float)delta;
        int shown = Mathf.Min((int)_charsRevealed, _totalChars);
        VisibleCharacters = shown;

        if (shown >= _totalChars)
        {
            _isAnimating = false;
            EmitSignal(SignalName.AnimationCompleted);
        }
    }
}
