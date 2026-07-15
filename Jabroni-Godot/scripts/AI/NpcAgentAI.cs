using Jabroni.Nav;

namespace Jabroni.AI;

public partial class NpcAgentAI : AgentAI
{
    private const string PatrolPathDataFile = "res://data/NavPath_Rocky.txt";

    protected override string ConfigId => "AC.Rocky";

    public override void _Ready()
    {
        PatrolPath = NavPathLoader.Load(PatrolPathDataFile);
        base._Ready();
    }

    protected override AIStateMachine CreateStateMachine() => new AIStateMachine_NPC(this);
}
