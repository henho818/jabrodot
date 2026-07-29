namespace Jabroni.Data;

/// <summary>
/// Column names and magic values shared by the dialogue tables. DialogBox reads them at runtime
/// and DialogGraph reads them when building the authoring graph -- if a slot column is ever
/// added here, both pick it up, so the validator can't quietly disagree with the game about
/// which lines a Dialog actually has.
/// </summary>
public static class DialogSchema
{
    /// <summary>A SubDialog's Next value meaning "close the box" rather than "jump to this Dialog".</summary>
    public const string EndCommand = "<end>";

    /// <summary>Locales that Localization.tsv is expected to carry a column for.</summary>
    public static readonly string[] Locales = { "en", "zh", "ja", "es" };

    /// <summary>The locale used for preview text in editor tooling.</summary>
    public const string PreviewLocale = "en";

    // Dialog_Dialog.txt
    public const string DialogIdColumn = "DialogID";
    public const string AvatarSheetColumn = "AvatarSheet";
    public const string AvatarIndexColumn = "AvatarIndex";

    /// <summary>The only sheet the game ships; written into AvatarSheet when authoring a portrait Dialog.</summary>
    public const string DefaultAvatarSheet = "avatars.png";

    /// <summary>
    /// The fixed SubDialog slots on a Dialog row, in cascade order. A Dialog shows at most this
    /// many lines; empty slots are skipped rather than treated as a terminator.
    /// <para>
    /// Widening this is half the change: Dialog_Dialog.txt's header has to gain the matching
    /// columns too, because TsvDocument takes its column set from the file and silently drops a
    /// write to a column the header doesn't have.
    /// </para>
    /// </summary>
    public static readonly string[] SubDialogSlotColumns =
    {
        "SubDialogID0", "SubDialogID1", "SubDialogID2", "SubDialogID3", "SubDialogID4",
        "SubDialogID5", "SubDialogID6", "SubDialogID7", "SubDialogID8", "SubDialogID9"
    };

    // Dialog_SubDialog.txt
    public const string LocalizationIdColumn = "LocalizationDialogID";
    public const string StyleColumn = "Style";
    public const string NextColumn = "Next";
    public const string ItemAwardColumn = "ItemAward";
    public const string ItemDependencyColumn = "ItemDependency";
    public const string PitchColumn = "Pitch";

    /// <summary>
    /// The AgentAI export that names the Dialog an agent opens -- the dialogue graph's entry
    /// points. It lives on the node rather than in Agent_Config.txt so one agent scene can say
    /// different things in different scenes, which is why DialogEntryPointScanner reads scenes
    /// to find the graph's roots. Must match the property name on AgentAI.
    /// </summary>
    public const string ChatDialogIdProperty = "ChatDialogId";
}
