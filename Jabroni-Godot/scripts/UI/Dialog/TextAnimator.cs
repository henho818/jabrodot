using System.Collections.Generic;
using System.Text;
using Godot;
using Jabroni.Settings;

namespace Jabroni.UI.Dialog;

/// <summary>
/// Typewriter reveal for a RichTextLabel: advances VisibleCharacters over time, playing a
/// typing sound every couple of non-whitespace characters (matching the source project's
/// TextAnimator: CharsPerSecond 16.7, a sound every 2 chars, silence on whitespace). Reveal
/// speed is scaled live by SettingsService.DialogPlaybackSpeedFactor so the speed hotkeys take
/// effect mid-line, not just on the next line.
///
/// Sizing deliberately does NOT use RichTextLabel's own fit_content: that measures the full
/// underlying Text regardless of VisibleCharacters, so the box would jump straight to its final
/// width on frame one instead of growing with the reveal. Instead CustomMinimumSize is driven
/// by hand every frame from a direct font measurement of just the revealed substring -- mirrors
/// the source project's TMP box, which tracks only currently-visible glyph bounds.
///
/// Text wider than <see cref="MaxTextWidth"/> is broken into rows up front, in <see cref="Play"/>,
/// and those breaks are baked into the text as newlines. The label's own autowrap is deliberately
/// left off: it would re-decide the breaks from whatever is revealed at that instant, so a word
/// destined for the next row starts printing at the end of the current one and jumps down
/// mid-word once it stops fitting. Fixed breaks mean a word begins on its final row from its
/// first glyph, and the box is free to stay as narrow as what's been revealed.
/// </summary>
public partial class TextAnimator : RichTextLabel
{
    [Signal]
    public delegate void AnimationCompletedEventHandler();

    private const float BaseCharsPerSecond = 16.7f;
    private const int CharsPerSoundTrigger = 2;

    /// <summary>
    /// How long the box takes to settle on a new width. Doubles as how far ahead the row count is
    /// read (see <see cref="AdvanceReveal"/>), i.e. how early the box opens the next row before a
    /// glyph lands on it. 0 disables the easing and snaps.
    /// <para>
    /// The vertical movement that follows a new row is ScrollableBox.VerticalEaseDuration's, not
    /// this one -- so if the box should have finished moving by the time the text arrives, this
    /// wants to be at least that long.
    /// </para>
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01,or_greater")]
    public float ResizeDuration { get; set; } = 0.125f;

    /// <summary>Gap still left when the resize is called settled -- close enough to be a sub-pixel.</summary>
    private const float ResizeResidual = 0.02f;

    /// <summary>
    /// Decay rate that leaves <see cref="ResizeResidual"/> of the gap after
    /// <see cref="ResizeDuration"/>.
    /// </summary>
    private float ResizeRate => -Mathf.Log(ResizeResidual) / ResizeDuration;

    /// <summary>Width cap as a fraction of the viewport's width -- keeps a margin at both edges.</summary>
    private const float MaxViewportWidthFraction = 0.9f;

    /// <summary>
    /// Width cap as a fraction of the viewport's *height*, applied alongside the width one so the
    /// lower of the two wins. Height stands in for a readable measure: on a wide screen the width
    /// fraction alone would stretch a line into an uncomfortably long read.
    /// </summary>
    private const float MaxViewportHeightFraction = 1.0f;

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
    private Font _font;
    private int _fontSize;
    private float _lineHeight;

    private float _charsRevealed;
    private int _totalChars;
    private bool _isAnimating;
    private int _charsSincePlayedSound;

    /// <summary>Width budget for the text itself, resolved once per line in <see cref="Play"/>.</summary>
    private float _maxTextWidth;

    /// <summary>
    /// Where <see cref="_displayedSize"/> is headed. Its height runs one <see cref="ResizeDuration"/>
    /// of typing ahead of the cursor, so it can be a row taller than the revealed text needs.
    /// </summary>
    private Vector2 _targetSize;

    /// <summary>The eased size actually handed to the layout, so the panel glides instead of snapping.</summary>
    private Vector2 _displayedSize;

    public bool IsAnimating => _isAnimating;

    public override void _Ready()
    {
        _typingSounds ??= LoadTypingSounds();

        _typingPlayer = new AudioStreamPlayer { Bus = "Dialog" };
        AddChild(_typingPlayer);

        _font = GetThemeFont("normal_font");
        _fontSize = GetThemeFontSize("normal_font_size");
        _lineHeight = _font.GetHeight(_fontSize);

        // The eased width trails the text slightly while it types, so the label is left unclipped
        // rather than cutting the glyph being typed. The line's padding absorbs a trail smaller
        // than itself; a longer one prints a little outside the panel instead of disappearing.
        ClipContents = false;

        // Reserve this line's (fixed) row height immediately, at zero width, even before Play()
        // is called -- lets DialogBox make a newly-revealed-but-not-yet-typing line's panel
        // Visible to grow the dialog box's height first, then start typing once that settles.
        _targetSize = new Vector2(0f, _lineHeight);
        _displayedSize = _targetSize;
        CustomMinimumSize = _targetSize;
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

    /// <summary>
    /// How wide the text may get: the lower of the width and height budgets, less whatever
    /// padding SubDialogLine puts around this label. The padding is read back from the theme
    /// rather than hardcoded, so restyling the line keeps the finished box inside the budget
    /// instead of overshooting it.
    /// <para>
    /// Taking the lower of the two means the height budget governs on anything wider than about
    /// 10:9, which is every ordinary display -- the width fraction only takes over on a viewport
    /// taller than it is wide.
    /// </para>
    /// </summary>
    private float MaxTextWidth()
    {
        Vector2 viewport = GetViewportRect().Size;
        float budget = Mathf.Min(
            viewport.X * MaxViewportWidthFraction,
            viewport.Y * MaxViewportHeightFraction);

        return Mathf.Max(1f, budget - HorizontalPadding());
    }

    /// <summary>
    /// The padding SubDialogLine puts either side of this label -- its MarginContainer's
    /// constants plus the panel stylebox's content margins. Read from the theme rather than
    /// hardcoded, so restyling the line keeps the finished box inside the width budget.
    /// </summary>
    private float HorizontalPadding()
    {
        if (GetParent() is not MarginContainer margin)
        {
            return 0f;
        }

        float padding = margin.GetThemeConstant("margin_left") + margin.GetThemeConstant("margin_right");

        if (margin.GetParent() is PanelContainer panel)
        {
            var styleBox = panel.GetThemeStylebox("panel");
            padding += styleBox.GetMargin(Side.Left) + styleBox.GetMargin(Side.Right);
        }

        return padding;
    }

    public void Play(string fullText)
    {
        // The row breaks are decided here, from the whole text, and baked in as newlines. Leaving
        // them to the label's own autowrap instead would decide them from whatever is revealed so
        // far, so a word that belongs on the next row starts printing at the end of this one and
        // jumps down mid-word once it no longer fits.
        _maxTextWidth = MaxTextWidth();
        Text = WrapToWidth(fullText, _maxTextWidth);

        VisibleCharacters = 0;
        _totalChars = GetTotalCharacterCount();
        _charsRevealed = 0f;
        _charsSincePlayedSound = 0;
        _isAnimating = _totalChars > 0;

        // Starts exact rather than easing in from whatever the previous text left behind.
        UpdateRevealedSize(0, 0);
        _displayedSize = _targetSize;
        CustomMinimumSize = _displayedSize;

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
        UpdateRevealedSize(_totalChars, _totalChars);
        EmitSignal(SignalName.AnimationCompleted);
    }

    public override void _Process(double delta)
    {
        if (_isAnimating)
        {
            AdvanceReveal(delta);
        }

        // After the reveal, so the ease (and its trailing limit) act on this frame's target
        // rather than last frame's. Runs even when nothing is revealing, since the box is still
        // easing onto the final size for a moment after the last character lands.
        EaseTowardTargetSize(delta);
    }

    private void AdvanceReveal(double delta)
    {
        float speedFactor = SettingsService.Instance?.DialogPlaybackSpeedFactor ?? 1f;
        int previouslyShown = Mathf.Min((int)_charsRevealed, _totalChars);

        float charsPerSecond = BaseCharsPerSecond * speedFactor;
        _charsRevealed += charsPerSecond * (float)delta;

        int shown = Mathf.Min((int)_charsRevealed, _totalChars);
        VisibleCharacters = shown;

        // Where the cursor will be once a resize started now would have finished.
        int anticipated = Mathf.Min(
            _totalChars,
            Mathf.CeilToInt(_charsRevealed + (charsPerSecond * ResizeDuration)));

        UpdateRevealedSize(shown, anticipated);

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

    /// <summary>
    /// Greedily breaks the text into rows no wider than the cap, so <see cref="Play"/> can bake
    /// the breaks in as real newlines. A chunk wider than the cap on its own gets a row to itself
    /// and overhangs it, the same as any word-wrapping would do.
    /// <para>
    /// Rows are assembled from chunks rather than space-delimited words. Chinese and Japanese
    /// don't put spaces between words, so splitting on whitespace hands back the entire line as
    /// one unbreakable token, and it runs straight off the box no matter how narrow the cap is.
    /// <see cref="SplitIntoChunks"/> decides where a break is permitted at all; this method only
    /// picks which of those opportunities to take.
    /// </para>
    /// </summary>
    private string WrapToWidth(string text, float maxWidth)
    {
        var rows = new List<string>();
        string row = "";

        foreach (var chunk in SplitIntoChunks(text))
        {
            string candidate = row.Length == 0
                ? chunk.Text
                : row + (chunk.FollowsSpace ? " " : "") + chunk.Text;

            if (row.Length > 0 && _font.GetStringSize(candidate, HorizontalAlignment.Left, -1, _fontSize).X > maxWidth)
            {
                rows.Add(row);
                row = chunk.Text;
            }
            else
            {
                row = candidate;
            }
        }

        rows.Add(row);
        return string.Join('\n', rows);
    }

    /// <summary>A run of text that must stay on one row, and whether a dropped space preceded it.</summary>
    private readonly record struct TextChunk(string Text, bool FollowsSpace);

    /// <summary>
    /// Cuts the text at every point a row is allowed to break, leaving pieces
    /// <see cref="WrapToWidth"/> can treat as atomic. A space is a break whose character is
    /// dropped (the row join puts it back); between CJK characters nearly every position is a
    /// break, which is exactly why those scripts need no spaces to begin with.
    /// </summary>
    private static List<TextChunk> SplitIntoChunks(string text)
    {
        var chunks = new List<TextChunk>();
        var current = new StringBuilder();
        bool followsSpace = false;

        void Flush()
        {
            if (current.Length > 0)
            {
                chunks.Add(new TextChunk(current.ToString(), followsSpace));
                current.Clear();
            }
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == ' ')
            {
                Flush();
                followsSpace = true;
                continue;
            }

            if (current.Length > 0 && AllowsBreakBetween(text[i - 1], c))
            {
                Flush();
                followsSpace = false;
            }

            current.Append(c);
        }

        Flush();
        return chunks;
    }

    /// <summary>
    /// Whether a row may break between two adjacent characters. Latin runs break only at spaces,
    /// which the caller handles; anything touching a wide-script character may break, subject to
    /// the kinsoku rules below.
    /// </summary>
    private static bool AllowsBreakBetween(char before, char after)
    {
        if (!IsWideScript(before) && !IsWideScript(after))
        {
            return false;
        }

        return !NoLineStart.Contains(after) && !NoLineEnd.Contains(before);
    }

    /// <summary>
    /// Punctuation that may not open a row -- it has to stay against the character it follows,
    /// or a line ends up starting with a stray comma. (Kinsoku shori, the same rule Japanese and
    /// Chinese typesetting has always used.)
    /// </summary>
    private const string NoLineStart = "、。，．：；！？）］｝」』】〉》〕・ー々ゝゞ…‥,.:;!?)]}";

    /// <summary>Punctuation that may not close a row, for the same reason in the other direction.</summary>
    private const string NoLineEnd = "（［｛「『【〈《〔([{";

    /// <summary>
    /// Whether a character belongs to a script that breaks between characters rather than between
    /// words -- Chinese, Japanese, Korean, and the full-width forms that travel with them.
    /// </summary>
    private static bool IsWideScript(char c)
    {
        return c is (>= '\u3000' and <= '\u303F')  // CJK symbols and punctuation
            or (>= '\u3040' and <= '\u30FF')       // hiragana and katakana
            or (>= '\u3400' and <= '\u4DBF')       // CJK unified ideographs, extension A
            or (>= '\u4E00' and <= '\u9FFF')       // CJK unified ideographs
            or (>= '\uAC00' and <= '\uD7AF')       // hangul syllables
            or (>= '\uFF00' and <= '\uFF60')       // full-width forms
            or (>= '\uFFE0' and <= '\uFFE6');      // full-width symbols
    }

    // Width uses a pure ease-out: a fixed fraction of the remaining gap per unit time, fastest on
    // the first frame. Deliberately not a spring or any curve that eases in -- the width target
    // moves again with almost every character, so an ease-in phase would restart constantly and
    // the box would sit permanently in its slowest, furthest-behind stretch.
    //
    // Height isn't eased here at all: it steps straight onto the new row, so the space is there
    // the instant a glyph needs it. ScrollableBox is what eases onto that new height -- growing
    // the box upward, or scrolling once it has hit its cap -- which is a rare enough event to
    // afford a gentler curve than this one.
    private void EaseTowardTargetSize(double delta)
    {
        if (_displayedSize.IsEqualApprox(_targetSize))
        {
            return;
        }

        float width = Mathf.Lerp(_displayedSize.X, _targetSize.X, 1f - Mathf.Exp(-ResizeRate * (float)delta));

        // Settle exactly rather than approaching forever, so the layout stops being dirtied.
        if (Mathf.Abs(width - _targetSize.X) < 0.5f)
        {
            width = _targetSize.X;
        }

        _displayedSize = new Vector2(width, _targetSize.Y);
        CustomMinimumSize = _displayedSize;
    }

    // Rows are laid out by the newlines baked in at Play, so sizing is just a measurement: the
    // widest revealed row, by however many rows have been reached. Height alone reads ahead to
    // anticipatedChars, so the box opens the next row before the cursor gets there; width has no
    // equivalent, since it grows smoothly with each glyph anyway.
    private void UpdateRevealedSize(int shownChars, int anticipatedChars)
    {
        string parsedText = GetParsedText();

        string[] revealedRows = Rows(parsedText, shownChars);
        float width = 0f;

        foreach (string row in revealedRows)
        {
            width = Mathf.Max(width, _font.GetStringSize(row, HorizontalAlignment.Left, -1, _fontSize).X);
        }

        int anticipatedRows = Rows(parsedText, anticipatedChars).Length;

        // Only the target moves here; EaseTowardTargetSize is what the layout actually sees.
        _targetSize = new Vector2(width, anticipatedRows * _lineHeight);
    }

    private static string[] Rows(string parsedText, int charCount)
    {
        int clamped = Mathf.Clamp(charCount, 0, parsedText.Length);
        return (clamped > 0 ? parsedText.Substring(0, clamped) : string.Empty).Split('\n');
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
