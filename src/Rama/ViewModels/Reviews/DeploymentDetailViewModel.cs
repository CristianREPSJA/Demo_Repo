using System;
using System.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Rama.Core.Reviews;

namespace SourceGit.ViewModels
{
    // Aprobación de etapa de pipeline. SUPUESTO: la API de approvals no trae las etapas de la ejecución,
    // así que se muestra solo la información de la aprobación (sin fila de etapas).
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
        }

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
