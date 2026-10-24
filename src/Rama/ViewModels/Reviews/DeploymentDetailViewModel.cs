using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Rama.Core.Azure;
using Rama.Core.Reviews;

namespace SourceGit.ViewModels
{
    // Aprobación de etapa de pipeline. Verificado 2026-10-23: la aprobación trae pipeline.owner.id (id de la
    // ejecución) y las etapas salen del timeline de build; se cargan aparte y sin bloquear la decisión.
    public partial class DeploymentDetailViewModel : ReviewDetailViewModel
    {
        public string ApproversText { get; }
        public string Instructions { get; }
        public bool HasInstructions => Instructions.Length > 0;

        [ObservableProperty]
        private string _comment = string.Empty;

        public DeploymentDetailViewModel(ReviewListItem item, ReviewsService service, string myId, Func<Task> refreshList)
            : base(item, service, myId, refreshList)
        {
            var steps = item.Approval?.Steps ?? [];
            ApproversText = App.Text("Rama.Reviews.Deploy.Approvers",
                string.Join(", ", steps.Select(s => s.ApproverName).Where(n => n.Length > 0).Distinct()));
            Instructions = item.Approval?.Instructions ?? string.Empty;
            _runId = item.Approval?.RunId;
            if (_runId is not null)
                _ = LoadStagesAsync(_runId.Value);
        }

        public IReadOnlyList<StageRowViewModel> Stages { get; private set; } = [];
        public bool HasStages => Stages.Count > 0;
        public bool HasStagesError => StagesError.Length > 0;

        [ObservableProperty]
        private string _stagesError = string.Empty;

        partial void OnStagesErrorChanged(string value) => OnPropertyChanged(nameof(HasStagesError));

        // Error no fatal: solo oculta la fila de etapas y muestra un aviso; aprobar/rechazar sigue disponible.
        private async Task LoadStagesAsync(int runId)
        {
            try
            {
                var stages = await Service.LoadRunStagesAsync(runId);
                Stages = stages.Select(s => new StageRowViewModel(s)).ToList();
                OnPropertyChanged(nameof(Stages));
                OnPropertyChanged(nameof(HasStages));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StagesError = App.Text("Rama.Reviews.Deploy.Stages.Error", ex.Message);
            }
        }

        private readonly int? _runId;

        [RelayCommand]
        private void Approve() =>
            AskConfirm(App.Text("Rama.Reviews.Confirm.ApproveDeploy", Title), _ => DecideAsync(true));

        [RelayCommand]
        private void Reject() =>
            AskConfirm(App.Text("Rama.Reviews.Confirm.RejectDeploy", Title), _ => DecideAsync(false));

        private async Task DecideAsync(bool approve)
        {
            await Service.DecideDeploymentAsync(Item.Approval, approve, Comment);
            Comment = string.Empty;
            await RefreshList();
        }
    }
}
