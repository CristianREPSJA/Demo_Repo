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
    public partial class PullRequestDetailViewModel : ReviewDetailViewModel
    {
        public IReadOnlyList<PolicyRowViewModel> Policies { get; private set; } = [];
        public IReadOnlyList<ReviewerRowViewModel> Reviewers { get; private set; } = [];
        public VoteOption PrimaryVote { get; } = ReviewMapper.VoteOptions[0];
        public IReadOnlyList<VoteOption> OtherVotes { get; } = ReviewMapper.VoteOptions.Skip(1).ToList();
        public bool CanVote => Item.Kind != ReviewKind.MyPullRequest;

        [ObservableProperty]
        private PullRequestDetail _detail;

        [ObservableProperty]
        private int _selectedTab;

        public bool HasDetail => Detail != null;
        public bool IsSummaryTab => SelectedTab == 0;
        public bool IsOtherTab => SelectedTab != 0;
        public string Headline => Detail == null ? string.Empty :
            App.Text("Rama.Reviews.Wants", Detail.Pr.CreatedByName, Detail.CommitsText, Detail.Source, Detail.Target);
        public string PoliciesTitle => App.Text("Rama.Reviews.Policies", Detail?.Target ?? string.Empty);
        public bool HasPolicies => Policies.Count > 0;
        public string Description => string.IsNullOrWhiteSpace(Detail?.Pr.Description) ? App.Text("Rama.Reviews.NoDescription") : Detail.Pr.Description;
        public bool CanComplete => Detail is { CanComplete: true };
        public string CompleteReason => Detail?.CompleteBlockReason ?? string.Empty;
        public bool HasCompleteReason => CompleteReason.Length > 0;
        public string CommitsCount => Detail?.CommitCount.ToString() ?? string.Empty;
        public string WebUrl => Item.Pr?.WebUrl ?? string.Empty;

        public PullRequestDetailViewModel(ReviewListItem item, ReviewsService service, string myId, Func<Task> refreshList)
            : base(item, service, myId, refreshList)
        {
            _ = LoadAsync();
        }

        partial void OnDetailChanged(PullRequestDetail value)
        {
            Policies = value?.Policies.Select(p => new PolicyRowViewModel(p)).ToList() ?? [];
            Reviewers = value?.Reviewers.Select(r => new ReviewerRowViewModel(r)).ToList() ?? [];
            foreach (var n in new[]
            {
                nameof(Policies), nameof(Reviewers), nameof(HasDetail), nameof(Headline), nameof(PoliciesTitle),
                nameof(HasPolicies), nameof(Description), nameof(CanComplete), nameof(CompleteReason),
                nameof(HasCompleteReason), nameof(CommitsCount),
            })
                OnPropertyChanged(n);
        }

        partial void OnSelectedTabChanged(int value)
        {
            OnPropertyChanged(nameof(IsSummaryTab));
            OnPropertyChanged(nameof(IsOtherTab));
        }

        private async Task LoadAsync()
        {
            IsLoading = true;
            ClearError();
            try
            {
                Detail = await Service.LoadDetailAsync(Item.Pr, MyId);
            }
            catch (Exception ex)
            {
                SetError(ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private Task RetryAsync() => LoadAsync();

        [RelayCommand]
        private void SelectTab(int tab) => SelectedTab = tab;

        [RelayCommand]
        private void OpenInAzure()
        {
            if (WebUrl.Length > 0)
                Native.OS.OpenBrowser(WebUrl);
        }

        [RelayCommand]
        private Task VoteAsync(VoteOption option)
        {
            if (option.NeedsConfirm)
            {
                AskConfirm(App.Text("Rama.Reviews.Confirm.RejectPr"), _ => CastVoteAsync(option.Vote));
                return Task.CompletedTask;
            }

            return RunAsync(() => CastVoteAsync(option.Vote));
        }

        private async Task CastVoteAsync(Vote vote)
        {
            await Service.VoteAsync(Item.Pr, MyId, vote);
            await LoadAsync();
            await RefreshList();
        }
    }
}
