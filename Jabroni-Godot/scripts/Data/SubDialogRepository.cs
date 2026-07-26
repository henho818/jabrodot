namespace Jabroni.Data;

public partial class SubDialogRepository : TsvRepository
{
    protected override string DataFilePath => DataPaths.SubDialog;
}
