using Godot;
using Jabroni.Settings;

namespace Jabroni.UI.Dialog;

/// <summary>
/// Typewriter reveal for a RichTextLabel: advances VisibleCharacters over time, playing a
/// typing sound every couple of non-whitespace characters (matching the source project's
/// TextAnimator: CharsPerSecond 16.7, a sound every 2 chars, silence on whitespace). Reveal
/// speed is scaled live by SettingsService.DialogPlaybackSpeedFactor so the speed hotkeys take
/// effect mid-line, not just on the next line.
/// </summary>
public partial class TextAnimator : RichTextLabel
{
    [Signal]
    public delegate void AnimationCompletedEventHandler();

    private const float BaseCharsPerSecond = 16.7f;
    private const int CharsPerSoundTrigger = 2;

    private static readonly string[] TypingSoundPaths =
    {
        "res://audio/type/410.wav",
        "res://audio/type/411.wav",
        "res://audio/type/414.wav",
        "res://audio/type/415.wav",
    };

    private static AudioStream[] _typingSounds;

    private readonly RandomNumberGenerator _rng = new();
    private AudioStreamPlayer _typingPlayer;

    private float _charsRevealed;
    private int _totalChars;
    private bool _isAnimating;
    private int _charsSincePlayedSound;

    public bool IsAnimating => _isAnimating;

    public override void _Ready()
    {
        _typingSounds ??= LoadTypingSounds();

        _typingPlayer = new AudioStreamPlayer { Bus = "Dialog" };
        AddChild(_typingPlayer);
    }

    private static AudioStream[] LoadTypingSounds()
    {
        var sounds = new AudioStream[TypingSoundPaths.Length];
        for (int i = 0; i < TypingSoundPaths.Length; i++)
        {
            sounds[i] = GD.Load<AudioStream>(TypingSoundPaths[i]);
        }

        return sounds;
    }

    /// <summary>Sets the typing sound's pitch scale for the line about to play (per-line authored data).</summary>
    public void SetPitch(float pitchScale)
    {
        _typingPlayer.PitchScale = pitchScale;
    }

    public void Play(string fullText)
    {
        Text = fullText;
        VisibleCharacters = 0;
        _totalChars = GetTotalCharacterCount();
        _charsRevealed = 0f;
        _charsSincePlayedSound = 0;
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

        float speedFactor = SettingsService.Instance?.DialogPlaybackSpeedFactor ?? 1f;
        int previouslyShown = Mathf.Min((int)_charsRevealed, _totalChars);

        _charsRevealed += BaseCharsPerSecond * speedFactor * (float)delta;
        int shown = Mathf.Min((int)_charsRevealed, _totalChars);
        VisibleCharacters = shown;

        if (shown > previouslyShown)
        {
            string parsedText = GetParsedText();
            for (int i = previouslyShown; i < shown && i < parsedText.Length; i++)
            {
                OnCharacterShown(parsedText[i]);
            }
        }

        if (shown >= _totalChars)
        {
            _isAnimating = false;
            EmitSignal(SignalName.AnimationCompleted);
        }
    }

    private void OnCharacterShown(char shownChar)
    {
        if (char.IsWhiteSpace(shownChar))
        {
            _charsSincePlayedSound = 0;
            return;
        }

        _charsSincePlayedSound++;
        if (_charsSincePlayedSound < CharsPerSoundTrigger)
        {
            return;
        }

        _charsSincePlayedSound = 0;
        _typingPlayer.Stream = _typingSounds[_rng.RandiRange(0, _typingSounds.Length - 1)];
        _typingPlayer.Play();
    }
}
