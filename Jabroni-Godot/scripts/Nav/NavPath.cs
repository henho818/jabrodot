using System.Collections.Generic;

namespace Jabroni.Nav;

public sealed class NavPath
{
    public IReadOnlyList<NavNode> Nodes { get; }
    public bool Looping { get; }

    public NavPath(IReadOnlyList<NavNode> nodes, bool looping)
    {
        Nodes = nodes;
        Looping = looping;
    }
}
