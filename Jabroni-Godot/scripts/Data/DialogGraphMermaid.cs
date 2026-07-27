using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jabroni.Data;

/// <summary>
/// Renders a <see cref="DialogGraph"/> as a Mermaid flowchart. The in-editor GraphEdit view is
/// for authoring; this is for everything outside the editor -- pasting the shape of a
/// conversation into a design doc, a commit message, or a review, where a picture of the
/// branches beats reading four TSV files side by side.
/// </summary>
public static class DialogGraphMermaid
{
    private const int MaxLabelLength = 42;
    private const string EndNodeId = "DIALOG_END";

    public static string Export(DialogGraph graph)
    {
        var builder = new StringBuilder();
        builder.AppendLine("flowchart LR");

        AppendEntryPoints(graph, builder);
        AppendDialogs(graph, builder);
        AppendEdges(graph, builder);
        AppendStyling(graph, builder);

        return builder.ToString();
    }

    private static void AppendEntryPoints(DialogGraph graph, StringBuilder builder)
    {
        if (graph.EntryPoints.Count == 0)
        {
            return;
        }

        builder.AppendLine("    %% entry points (AgentAI.ChatDialogId, per scene)");
        foreach (var entry in graph.EntryPoints)
        {
            string agentLabel = string.IsNullOrEmpty(entry.Description) ? entry.SourceId : entry.Description;
            builder.AppendLine($"    {NodeId(entry.SourceId)}([{Quote(agentLabel)}]) --> {NodeId(entry.DialogId)}");
        }

        builder.AppendLine();
    }

    private static void AppendDialogs(DialogGraph graph, StringBuilder builder)
    {
        builder.AppendLine("    %% dialogs");
        foreach (var dialog in OrderedDialogs(graph))
        {
            string speaker = dialog.HasPortrait ? $"avatar {dialog.AvatarIndex}" : "narrator";
            builder.AppendLine($"    {NodeId(dialog.DialogId)}[{Quote($"{dialog.DialogId}\\n({speaker})")}]");
        }

        if (graph.Dialogs.Values.SelectMany(d => d.Lines).Any(line => line.LinkKind == DialogLinkKind.End))
        {
            builder.AppendLine($"    {EndNodeId}(({Quote("end")}))");
        }

        builder.AppendLine();
    }

    private static void AppendEdges(DialogGraph graph, StringBuilder builder)
    {
        builder.AppendLine("    %% lines: each edge is one clickable SubDialog");
        foreach (var dialog in OrderedDialogs(graph))
        {
            foreach (var line in dialog.Lines)
            {
                string from = NodeId(dialog.DialogId);
                string label = Quote(Truncate(LineLabel(line)));

                switch (line.LinkKind)
                {
                    case DialogLinkKind.Jump:
                        builder.AppendLine($"    {from} -->|{label}| {NodeId(line.NextDialogId)}");
                        break;

                    case DialogLinkKind.End:
                        builder.AppendLine($"    {from} -->|{label}| {EndNodeId}");
                        break;

                    // A line with no Next is a statement, not a branch -- it has no edge to draw,
                    // so it's noted as a self-loop-free comment to keep the line visible in the
                    // export without implying a transition that doesn't happen.
                    case DialogLinkKind.None:
                        builder.AppendLine($"    %% {from} says {label} (no Next)");
                        break;
                }
            }
        }

        builder.AppendLine();
    }

    private static void AppendStyling(DialogGraph graph, StringBuilder builder)
    {
        var unreachable = graph.Dialogs.Keys.Except(graph.ReachableDialogIds()).OrderBy(id => id).ToList();
        if (unreachable.Count == 0)
        {
            return;
        }

        builder.AppendLine("    %% unreachable from any entry point");
        builder.AppendLine("    classDef unreachable stroke-dasharray: 4 4,opacity:0.6;");
        builder.AppendLine($"    class {string.Join(",", unreachable.Select(NodeId))} unreachable;");
    }

    private static IEnumerable<DialogNode> OrderedDialogs(DialogGraph graph)
    {
        return graph.Dialogs.Values.OrderBy(dialog => dialog.DialogId);
    }

    private static string LineLabel(DialogLineNode line)
    {
        if (!line.Resolved)
        {
            return $"?? {line.SubDialogId}";
        }

        return string.IsNullOrEmpty(line.PreviewText) ? line.SubDialogId : line.PreviewText;
    }

    /// <summary>
    /// Mermaid node ids have to be bare words. Every id in these tables has a dot in it, and
    /// entry-point ids also carry the scene file and node path separators.
    /// </summary>
    private static string NodeId(string id)
    {
        return new string(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }

    private static string Quote(string text)
    {
        return $"\"{text.Replace("\"", "#quot;")}\"";
    }

    private static string Truncate(string text)
    {
        string flattened = text.Replace('\n', ' ').Replace('\t', ' ').Trim();
        return flattened.Length <= MaxLabelLength ? flattened : flattened[..(MaxLabelLength - 1)] + "…";
    }
}
