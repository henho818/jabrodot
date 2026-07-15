namespace Jabroni.Data;

public partial class AgentConfigRepository : TsvRepository
{
    protected override string DataFilePath => "res://data/Agent_Config.txt";
}
