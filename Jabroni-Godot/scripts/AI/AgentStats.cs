using Jabroni.Data;

namespace Jabroni.AI;

/// <summary>Typed view over an Agent_Config.txt row.</summary>
public sealed class AgentStats
{
    public static readonly AgentStats Empty = new();

    public string Name { get; }
    public float BaseSpeed { get; }
    public float AttackDistance { get; }
    public float AlertDisengageTime { get; }
    public float SearchDisengageTime { get; }
    public float DetectionRadius { get; }
    public string ChatDialogId { get; }

    private AgentStats()
    {
        Name = "";
        ChatDialogId = "";
    }

    public AgentStats(TsvRow row)
    {
        Name = row.GetString("Name");
        BaseSpeed = row.GetFloat("BaseSpeed");
        AttackDistance = row.GetFloat("AttackDistance");
        AlertDisengageTime = row.GetFloat("AlertDisengageTime");
        SearchDisengageTime = row.GetFloat("SearchDisengageTime");
        DetectionRadius = row.GetFloat("DetectionRadius");
        ChatDialogId = row.GetString("DialogId");
    }
}
