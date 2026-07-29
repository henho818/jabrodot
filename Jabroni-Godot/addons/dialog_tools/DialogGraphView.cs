using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Jabroni.Data;

namespace Jabroni.Editor;

/// <summary>
/// Draws a DialogGraph as connected GraphNodes -- one node per Dialog, one row per SubDialog
/// line, one wire per Next jump -- and turns direct manipulation of it into edit intents.
/// <para>
/// The view never writes: dragging a wire raises <see cref="LinkRequested"/> and the panel
/// decides whether the edit is legal. That keeps the TSV documents the single source of truth,
/// so what's drawn is always what a rebuild from the data would draw.
/// </para>
/// </summary>
[Tool]
public partial class DialogGraphView : GraphEdit
{
    private const float ColumnSpacing = 420f;
    private const float RowSpacing = 260f;
    private const float MarginX = 60f;
    private const float MarginY = 40f;
    private const int MaxPreviewLength = 38;

    /// <summary>Metadata key tying a line's Button back to the SubDialog row it draws.</summary>
    private const string SubDialogIdMeta = "sub_dialog_id";

    private static readonly Color JumpColor = new("7fbfff");
    private static readonly Color EndColor = new("9fd97f");
    private static readonly Color InertColor = new("9a9a9a");
    private static readonly Color BrokenColor = new("ff7b6b");

    /// <summary>A line was dragged onto another Dialog: (subDialogId, targetDialogId).</summary>
    public event Action<string, string> LinkRequested;

    /// <summary>A wire was pulled off a line: (subDialogId).</summary>
    public event Action<string> UnlinkRequested;

    public event Action<IReadOnlyList<string>> DeleteDialogsRequested;

    /// <summary>Right-click on empty canvas -- the panel offers to create a Dialog.</summary>
    public event Action AddDialogRequested;

    /// <summary>Selection changed: (dialogId, subDialogId or null when only the node is selected).</summary>
    public event Action<string, string> SelectionChanged;

    // Godot node names can't contain '.', which every id in these tables uses, so the graph is
    // built against sanitised names and these map back.
    private readonly Dictionary<string, string> _nodeNameByDialogId = new();
    private readonly Dictionary<string, string> _dialogIdByNodeName = new();

    // Port index -> line, per node. Every line gets an output port (so any line can be dragged
    // into a jump), which makes the port index and the line index the same number.
    private readonly Dictionary<string, List<DialogLineNode>> _linesByNodeName = new();

    // Survives rebuilds so an edit doesn't throw away a layout the author arranged by hand.
    private readonly Dictionary<string, Vector2> _positionByDialogId = new();

    public string SelectedDialogId { get; private set; }
    public string SelectedSubDialogId { get; private set; }

    public override void _Ready()
    {
        // Left as false so dragging from an already-wired output rewires it in one gesture
        // (a line has exactly one Next, so there's nothing to preserve); pulling the wire off
        // the target's input port is what clears it.
        RightDisconnects = false;
        ConnectionRequest += OnConnectionRequest;
        DisconnectionRequest += OnDisconnectionRequest;
        DeleteNodesRequest += OnDeleteNodesRequest;
        PopupRequest += _ => AddDialogRequested?.Invoke();
    }

    public void Rebuild(DialogGraph graph)
    {
        CapturePositions();

        ClearConnections();
        foreach (var child in GetChildren().OfType<GraphNode>())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        _nodeNameByDialogId.Clear();
        _dialogIdByNodeName.Clear();
        _linesByNodeName.Clear();

        var depths = AssignDepths(graph);
        var perColumnCount = new Dictionary<int, int>();

        foreach (var dialog in graph.Dialogs.Values.OrderBy(d => d.DialogId))
        {
            int depth = depths[dialog.DialogId];
            int row = perColumnCount.GetValueOrDefault(depth);
            perColumnCount[depth] = row + 1;

            AddChild(BuildNode(graph, dialog, depth, row));
        }

        ConnectJumps(graph);
        RestoreSelection();
    }

    /// <summary>Scrolls the given Dialog into view and selects it -- used when an issue is clicked.</summary>
    public void FocusDialog(string dialogId)
    {
        if (!_nodeNameByDialogId.TryGetValue(dialogId, out string nodeName))
        {
            return;
        }

        var node = GetNodeOrNull<GraphNode>(nodeName);
        if (node == null)
        {
            return;
        }

        SelectOnly(node);
        SelectedDialogId = dialogId;
        SelectedSubDialogId = null;
        SelectionChanged?.Invoke(SelectedDialogId, null);

        ScrollOffset = (node.PositionOffset * Zoom) - (Size / 2f) + (node.Size * Zoom / 2f);
    }

    private void CapturePositions()
    {
        foreach (var node in GetChildren().OfType<GraphNode>())
        {
            if (_dialogIdByNodeName.TryGetValue(node.Name, out string dialogId))
            {
                _positionByDialogId[dialogId] = node.PositionOffset;
            }
        }
    }

    private void RestoreSelection()
    {
        if (SelectedDialogId == null || !_nodeNameByDialogId.ContainsKey(SelectedDialogId))
        {
            SelectedDialogId = null;
            SelectedSubDialogId = null;
            SelectionChanged?.Invoke(null, null);
            return;
        }

        var node = GetNodeOrNull<GraphNode>(_nodeNameByDialogId[SelectedDialogId]);
        if (node != null)
        {
            node.Selected = true;
        }

        // The selected line may have been removed by whatever edit triggered this rebuild.
        var lines = _linesByNodeName.GetValueOrDefault(_nodeNameByDialogId[SelectedDialogId]);
        if (lines != null && lines.All(line => line.SubDialogId != SelectedSubDialogId))
        {
            SelectedSubDialogId = null;
        }

        SelectionChanged?.Invoke(SelectedDialogId, SelectedSubDialogId);
    }

    private GraphNode BuildNode(DialogGraph graph, DialogNode dialog, int depth, int row)
    {
        string nodeName = SanitizeName(dialog.DialogId);
        _nodeNameByDialogId[dialog.DialogId] = nodeName;
        _dialogIdByNodeName[nodeName] = dialog.DialogId;
        _linesByNodeName[nodeName] = dialog.Lines.ToList();

        var node = new GraphNode
        {
            Name = nodeName,
            Title = dialog.HasPortrait
                ? $"{dialog.DialogId}  (avatar {dialog.AvatarIndex})"
                : $"{dialog.DialogId}  (narrator)",
            PositionOffset = _positionByDialogId.TryGetValue(dialog.DialogId, out var saved)
                ? saved
                : new Vector2(MarginX + (depth * ColumnSpacing), MarginY + (row * RowSpacing)),
            Draggable = true,
            Resizable = false,
        };

        node.NodeSelected += () => OnNodeSelected(dialog.DialogId);

        if (dialog.Lines.Count == 0)
        {
            node.AddChild(new Label { Text = "(no lines)", Modulate = BrokenColor });
            return node;
        }

        for (int rowIndex = 0; rowIndex < dialog.Lines.Count; rowIndex++)
        {
            var line = dialog.Lines[rowIndex];
            node.AddChild(BuildLineButton(graph, dialog.DialogId, line));

            // Every line gets an output port so any of them can be dragged into a jump; only the
            // first row gets the input port, which is what an incoming Next lands on.
            node.SetSlot(
                rowIndex,
                enableLeftPort: rowIndex == 0,
                typeLeft: 0,
                colorLeft: JumpColor,
                enableRightPort: true,
                typeRight: 0,
                colorRight: LineColor(graph, line));
        }

        return node;
    }

    private Button BuildLineButton(DialogGraph graph, string dialogId, DialogLineNode line)
    {
        string marker = line.LinkKind switch
        {
            DialogLinkKind.End => "■",
            DialogLinkKind.Jump => "→",
            _ => "·",
        };

        var button = new Button
        {
            Text = $"{marker} {Preview(line)}",
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            TooltipText = BuildTooltip(line),
            Modulate = LineColor(graph, line),
            ToggleMode = true,
            ButtonPressed = line.SubDialogId == SelectedSubDialogId && dialogId == SelectedDialogId,
        };

        button.SetMeta(SubDialogIdMeta, line.SubDialogId);
        button.Pressed += () => OnLineSelected(dialogId, line.SubDialogId);
        return button;
    }

    private void OnNodeSelected(string dialogId)
    {
        SelectedDialogId = dialogId;
        SelectedSubDialogId = null;
        SelectionChanged?.Invoke(dialogId, null);
    }

    private void OnLineSelected(string dialogId, string subDialogId)
    {
        SelectedDialogId = dialogId;
        SelectedSubDialogId = subDialogId;

        var node = GetNodeOrNull<GraphNode>(_nodeNameByDialogId.GetValueOrDefault(dialogId, ""));
        if (node != null)
        {
            SelectOnly(node);
        }

        // Only one line reads as "selected" at a time, across every node.
        foreach (var other in GetChildren().OfType<GraphNode>())
        {
            foreach (var button in other.GetChildren().OfType<Button>())
            {
                bool isSelected = other == node
                                  && button.HasMeta(SubDialogIdMeta)
                                  && button.GetMeta(SubDialogIdMeta).AsString() == subDialogId;
                button.SetPressedNoSignal(isSelected);
            }
        }

        SelectionChanged?.Invoke(dialogId, subDialogId);
    }

    private void SelectOnly(GraphNode node)
    {
        foreach (var other in GetChildren().OfType<GraphNode>())
        {
            other.Selected = other == node;
        }
    }

    private void OnConnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        var line = LineAt(fromNode, (int)fromPort);
        if (line != null && _dialogIdByNodeName.TryGetValue(toNode, out string targetDialogId))
        {
            LinkRequested?.Invoke(line.SubDialogId, targetDialogId);
        }
    }

    private void OnDisconnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        var line = LineAt(fromNode, (int)fromPort);
        if (line != null)
        {
            UnlinkRequested?.Invoke(line.SubDialogId);
        }
    }

    private void OnDeleteNodesRequest(Godot.Collections.Array<StringName> nodeNames)
    {
        var dialogIds = nodeNames
            .Select(name => _dialogIdByNodeName.GetValueOrDefault(name.ToString()))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        if (dialogIds.Count > 0)
        {
            DeleteDialogsRequested?.Invoke(dialogIds);
        }
    }

    private DialogLineNode LineAt(StringName nodeName, int port)
    {
        var lines = _linesByNodeName.GetValueOrDefault(nodeName.ToString());
        return lines != null && port >= 0 && port < lines.Count ? lines[port] : null;
    }

    private static string BuildTooltip(DialogLineNode line)
    {
        if (!line.Resolved)
        {
            return $"{line.SubDialogId}\n(no row in Dialog_SubDialog.txt)";
        }

        string destination = line.LinkKind switch
        {
            DialogLinkKind.End => "closes the box",
            DialogLinkKind.Jump => $"jumps to {line.NextDialogId}",
            _ => "goes nowhere (no Next)",
        };

        string award = string.IsNullOrEmpty(line.ItemAwardId) ? "" : $"\nawards: {line.ItemAwardId}";

        return $"{line.SubDialogId}\nkey: {line.LocalizationId}\nstyle: {line.StyleId}\n{destination}{award}";
    }

    private static Color LineColor(DialogGraph graph, DialogLineNode line)
    {
        if (!line.Resolved || string.IsNullOrEmpty(line.PreviewText))
        {
            return BrokenColor;
        }

        return line.LinkKind switch
        {
            DialogLinkKind.Jump => graph.Dialogs.ContainsKey(line.NextDialogId) ? JumpColor : BrokenColor,
            DialogLinkKind.End => EndColor,
            _ => InertColor,
        };
    }

    private void ConnectJumps(DialogGraph graph)
    {
        foreach (var dialog in graph.Dialogs.Values)
        {
            for (int port = 0; port < dialog.Lines.Count; port++)
            {
                var line = dialog.Lines[port];

                // Dangling jumps have no node to land on; they're already flagged red on the row
                // and reported by the validator, so they just go unwired here.
                if (line.LinkKind == DialogLinkKind.Jump
                    && _nodeNameByDialogId.TryGetValue(line.NextDialogId, out string targetName))
                {
                    ConnectNode(_nodeNameByDialogId[dialog.DialogId], port, targetName, 0);
                }
            }
        }
    }

    // Column = how many jumps it takes to reach a Dialog from an agent in a scene. Anything the BFS
    // never reaches is parked in one extra column past the deepest reachable one, so orphaned
    // branches are visibly off to the side rather than tangled into the main flow.
    private static Dictionary<string, int> AssignDepths(DialogGraph graph)
    {
        var depths = new Dictionary<string, int>();
        var pending = new Queue<string>();

        foreach (var entry in graph.EntryPoints.Where(entry => graph.Dialogs.ContainsKey(entry.DialogId)))
        {
            if (depths.TryAdd(entry.DialogId, 0))
            {
                pending.Enqueue(entry.DialogId);
            }
        }

        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            foreach (var line in graph.Dialogs[current].Lines)
            {
                string next = line.NextDialogId;
                if (!string.IsNullOrEmpty(next) && graph.Dialogs.ContainsKey(next) && depths.TryAdd(next, depths[current] + 1))
                {
                    pending.Enqueue(next);
                }
            }
        }

        int orphanColumn = depths.Count > 0 ? depths.Values.Max() + 1 : 0;
        foreach (string dialogId in graph.Dialogs.Keys)
        {
            depths.TryAdd(dialogId, orphanColumn);
        }

        return depths;
    }

    private static string Preview(DialogLineNode line)
    {
        string text = !line.Resolved || string.IsNullOrEmpty(line.PreviewText)
            ? line.SubDialogId
            : line.PreviewText;

        text = text.Replace('\n', ' ').Trim();
        return text.Length <= MaxPreviewLength ? text : text[..(MaxPreviewLength - 1)] + "…";
    }

    private static string SanitizeName(string id)
    {
        foreach (char invalid in new[] { '.', ':', '@', '/', '%', '"' })
        {
            id = id.Replace(invalid, '_');
        }

        return id;
    }
}
