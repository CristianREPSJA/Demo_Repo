using Avalonia.Media;

using Rama.Core.Azure;
using Rama.Core.Reviews;

using SourceGit.Rama;

namespace SourceGit.ViewModels
{
    // Fila de etapa de la ejecución: nombre + pastilla de estado.
    public sealed class StageRowViewModel(RunStage stage)
    {
        private Tone Tone => stage.Status switch
        {
            RunStageStatus.Succeeded => Tone.Good,
            RunStageStatus.Failed => Tone.Bad,
            RunStageStatus.WaitingForApproval => Tone.Warn,
            _ => Tone.Neutral,
        };

        public string Name => stage.Name;
        public string StatusText => App.Text("Rama.Reviews.Deploy.Stage." + stage.Status);
        public IBrush PillBackground => ToneBrushes.Background(Tone);
        public IBrush PillInk => ToneBrushes.Ink(Tone);
    }
}
