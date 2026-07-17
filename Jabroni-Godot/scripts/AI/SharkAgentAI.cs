namespace Jabroni.AI;

public partial class SharkAgentAI : AgentAI
{
    protected override string ConfigId => "AC.Shark";
    protected override double DefaultPatrolStayDuration => 1.0;
    protected override bool SnapPatrolPathToTerrain => false; // swims at a fixed depth, not on the terrain surface

    protected override AIStateMachine CreateStateMachine() => new AIStateMachine_Shark(this);
}
