using Jabroni.Nav;

namespace Jabroni.AI;

public partial class SharkAgentAI : AgentAI
{
    private const string PatrolPathDataFile = "res://data/NavPath_Shark.txt";

    protected override string ConfigId => "AC.Shark";

    public override void _Ready()
    {
        PatrolPath = NavPathLoader.Load(PatrolPathDataFile);
        base._Ready();
    }

    protected override AIStateMachine CreateStateMachine() => new AIStateMachine_Shark(this);
}
