namespace Jabroni.Data;

public partial class AgentConfigRepository : TsvRepository
{
    protected override string DataFilePath => DataPaths.AgentConfig;
}
