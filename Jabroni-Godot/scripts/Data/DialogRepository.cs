namespace Jabroni.Data;

public partial class DialogRepository : TsvRepository
{
    protected override string DataFilePath => DataPaths.Dialog;
}
