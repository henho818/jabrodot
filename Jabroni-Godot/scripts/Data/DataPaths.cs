namespace Jabroni.Data;

/// <summary>
/// The one place every TSV data file is named. The repositories load these at runtime and the
/// editor dialogue tooling loads the same files directly (it can't go through the autoloads,
/// which don't run in the editor), so keeping the paths here stops the two from drifting apart.
/// </summary>
public static class DataPaths
{
    public const string AgentConfig = "res://data/Agent_Config.txt";
    public const string Dialog = "res://data/Dialog_Dialog.txt";
    public const string SubDialog = "res://data/Dialog_SubDialog.txt";
    public const string SubDialogStyle = "res://data/Dialog_SubDialogStyle.txt";
    public const string Item = "res://data/Item_Item.txt";
    public const string Localization = "res://data/Localization.tsv";
}
