using System.Collections.Generic;
using Godot;
using Jabroni.Data;

namespace Jabroni.UI.Dialog;

/// <summary>
/// Top-level dialog window: resolves a Dialog's SubDialog lines from the data repositories,
/// cascades their typewriter reveals one after another, and advances/closes on click,
/// mirroring the source project's DialogBox/SubDialogBox cascade-and-click-to-advance model.
/// No avatar portrait yet (deferred until sprite loading exists); text-only for this milestone.
/// Exposes a single Instance (only one dialog box exists) so AI tasks can trigger/observe
/// it without needing a scene-path lookup.
/// </summary>
public partial class DialogBox : Control
{
    [Signal]
    public delegate void ClosedEventHandler();

    public static DialogBox Instance { get; private set; }

    private const string EndCommand = "<end>";
    private const string SubDialogLineScenePath = "res://scenes/UI/SubDialogLine.tscn";

    private static readonly string[] SubDialogSlotColumns =
    {
        "SubDialogID0", "SubDialogID1", "SubDialogID2", "SubDialogID3", "SubDialogID4", "SubDialogID5"
    };

    private VBoxContainer _lineContainer;
    private PackedScene _lineScene;
    private readonly List<SubDialogLine> _activeLines = new();

    public override void _Ready()
    {
        Instance = this;
        _lineContainer = GetNode<VBoxContainer>("Panel/Lines");
        _lineScene = GD.Load<PackedScene>(SubDialogLineScenePath);
        Visible = false;
    }

    public void TriggerDialog(string dialogId)
    {
        var dialogRepo = GetNode<DialogRepository>("/root/DialogRepository");
        var dialogRow = dialogRepo.Get(dialogId);
        if (dialogRow == null)
        {
            GD.PushWarning($"DialogBox: unknown dialog id '{dialogId}'");
            return;
        }

        ClearLines();
        BuildLines(dialogRow);

        Visible = true;
        StartCascadeFrom(0);
    }

    private void BuildLines(TsvRow dialogRow)
    {
        var subDialogRepo = GetNode<SubDialogRepository>("/root/SubDialogRepository");
        var styleRepo = GetNode<SubDialogStyleRepository>("/root/SubDialogStyleRepository");

        foreach (string column in SubDialogSlotColumns)
        {
            string subDialogId = dialogRow.GetString(column);
            if (string.IsNullOrEmpty(subDialogId))
            {
                continue;
            }

            var subDialogRow = subDialogRepo.Get(subDialogId);
            if (subDialogRow == null)
            {
                GD.PushWarning($"DialogBox: unknown sub-dialog id '{subDialogId}'");
                continue;
            }

            var styleRow = styleRepo.Get(subDialogRow.GetString("Style"));
            Color bg = styleRow != null ? styleRow.GetColor("BgColor") : Colors.White;
            Color textColor = styleRow != null ? styleRow.GetColor("TextColor") : Colors.Black;
            string localizedText = Tr(subDialogRow.GetString("LocalizationDialogID"));
            string next = subDialogRow.GetString("Next");

            var line = _lineScene.Instantiate<SubDialogLine>();
            _lineContainer.AddChild(line);
            line.Setup(localizedText, bg, textColor, next);
            line.Visible = false;
            line.AdvanceRequested += () => OnLineAdvanceRequested(line);

            _activeLines.Add(line);
        }
    }

    private void StartCascadeFrom(int index)
    {
        if (index >= _activeLines.Count)
        {
            return;
        }

        var line = _activeLines[index];
        line.Visible = true;
        line.PlayTyping(() => StartCascadeFrom(index + 1));
    }

    private void OnLineAdvanceRequested(SubDialogLine line)
    {
        string next = line.NextDialogId;
        if (next == EndCommand)
        {
            Close();
        }
        else if (!string.IsNullOrEmpty(next))
        {
            TriggerDialog(next);
        }
    }

    private void Close()
    {
        Visible = false;
        ClearLines();
        EmitSignal(SignalName.Closed);
    }

    private void ClearLines()
    {
        foreach (var line in _activeLines)
        {
            line.QueueFree();
        }

        _activeLines.Clear();
    }
}
