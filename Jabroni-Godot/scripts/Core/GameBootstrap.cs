using Godot;

namespace Jabroni.Core;

public partial class GameBootstrap : Node
{
    public override void _Ready()
    {
        InputActions.Register();
    }
}
