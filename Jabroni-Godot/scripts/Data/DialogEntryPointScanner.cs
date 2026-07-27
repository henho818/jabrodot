using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Jabroni.Data;

/// <summary>
/// Finds the dialogue graph's roots by reading every scene for AgentAI nodes that have a
/// ChatDialogId set. Since the id moved out of Agent_Config.txt and onto the node, the scenes
/// -- not a table -- are the only record of which Dialogs anything actually starts, so the
/// validator has to look there to tell a reachable branch from dead data.
/// <para>
/// Scenes are read through <see cref="PackedScene.GetState"/> rather than by parsing the .tscn
/// text, so this keeps working if the scene format changes. That does mean it sees the file on
/// disk: a ChatDialogId typed into the Inspector shows up here only once the scene is saved.
/// </para>
/// </summary>
public static class DialogEntryPointScanner
{
    /// <summary>Only game scenes are scanned; addons ship hundreds of .tscn files with no agents in them.</summary>
    public const string ScenesRoot = "res://scenes";

    public static List<DialogEntryPoint> Scan()
    {
        var entryPoints = new List<DialogEntryPoint>();

        foreach (string scenePath in FindScenes(ScenesRoot))
        {
            CollectFromScene(scenePath, entryPoints);
        }

        return entryPoints
            .OrderBy(entry => entry.SourceId, System.StringComparer.Ordinal)
            .ToList();
    }

    private static void CollectFromScene(string scenePath, List<DialogEntryPoint> entryPoints)
    {
        // A scene that fails to load (a broken dependency mid-refactor, say) shouldn't take the
        // whole Dialogue panel down with it -- the rest of the graph is still worth validating.
        var scene = ResourceLoader.Load<PackedScene>(scenePath);
        var state = scene?.GetState();
        if (state == null)
        {
            GD.PushWarning($"DialogEntryPointScanner: couldn't load {scenePath}; its agents are not counted as entry points.");
            return;
        }

        string sceneName = scenePath.GetFile();

        for (int node = 0; node < state.GetNodeCount(); node++)
        {
            string dialogId = FindChatDialogId(state, node);
            if (string.IsNullOrEmpty(dialogId))
            {
                continue;
            }

            var names = PathNames(state.GetNodePath(node));

            entryPoints.Add(new DialogEntryPoint
            {
                SourceId = $"{sceneName}:{string.Join('/', names)}",
                Description = $"{DescribeAgent(state, names)} in {sceneName}",
                DialogId = dialogId,
            });
        }
    }

    /// <summary>
    /// The property is only stored when it differs from the export's default, so an agent with
    /// nothing to say simply has no entry here -- which is what we want, since it opens no box.
    /// </summary>
    private static string FindChatDialogId(SceneState state, int node)
    {
        for (int property = 0; property < state.GetNodePropertyCount(node); property++)
        {
            if (state.GetNodePropertyName(node, property) == DialogSchema.ChatDialogIdProperty)
            {
                return state.GetNodePropertyValue(node, property).AsString();
            }
        }

        return "";
    }

    /// <summary>
    /// A SceneState path is relative to the scene root and spells that root as a literal "."
    /// segment, so "./AgentAI" comes back as two names. Dropping the "." leaves the path the
    /// author would actually type, and leaves the root itself as an empty list.
    /// </summary>
    private static IReadOnlyList<string> PathNames(NodePath nodePath)
    {
        var names = new List<string>();

        for (int name = 0; name < nodePath.GetNameCount(); name++)
        {
            string segment = nodePath.GetName(name);
            if (segment != ".")
            {
                names.Add(segment);
            }
        }

        return names;
    }

    /// <summary>
    /// Names the agent by its body node ("NPC") rather than by the AgentAI child, since the body
    /// is what the author recognises in the scene tree. An AgentAI sitting directly under the
    /// scene root has no parent segment in its path, so it takes the root's name instead.
    /// </summary>
    private static string DescribeAgent(SceneState state, IReadOnlyList<string> names)
    {
        return names.Count >= 2 ? names[^2] : state.GetNodeName(0);
    }

    private static IEnumerable<string> FindScenes(string directory)
    {
        using var dir = DirAccess.Open(directory);
        if (dir == null)
        {
            yield break;
        }

        foreach (string file in dir.GetFiles().OrderBy(name => name, System.StringComparer.Ordinal))
        {
            if (file.GetExtension() == "tscn" || file.GetExtension() == "scn")
            {
                yield return $"{directory}/{file}";
            }
        }

        foreach (string subDirectory in dir.GetDirectories().OrderBy(name => name, System.StringComparer.Ordinal))
        {
            foreach (string scene in FindScenes($"{directory}/{subDirectory}"))
            {
                yield return scene;
            }
        }
    }
}
