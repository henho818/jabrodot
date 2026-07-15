using Godot;
using Jabroni.Data;
using Jabroni.Localization;

namespace Jabroni.Core;

public partial class GameBootstrap : Node
{
    public override void _Ready()
    {
        InputActions.Register();
        LocalizationBootstrap.Load();
        DebugPrintDataPipelineSmokeTest();
    }

    // Temporary console verification for the M4 data pipeline, superseded once the real
    // Dialog UI (M5) and settings/language UI (M9) exist to exercise this visually.
    private void DebugPrintDataPipelineSmokeTest()
    {
        var subDialogRepo = GetNode<SubDialogRepository>("/root/SubDialogRepository");
        var row = subDialogRepo.Get("S.TestSpeech");
        if (row == null)
        {
            GD.PushWarning("[DataPipelineTest] S.TestSpeech not found.");
            return;
        }

        string locKey = row.GetString("LocalizationDialogID");

        TranslationServer.SetLocale("en");
        GD.Print($"[DataPipelineTest] en: {Tr(locKey)}");

        TranslationServer.SetLocale("es");
        GD.Print($"[DataPipelineTest] es: {Tr(locKey)}");

        TranslationServer.SetLocale("en");
    }
}
