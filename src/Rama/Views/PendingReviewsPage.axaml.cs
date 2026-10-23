using Avalonia.Controls;

namespace SourceGit.Views
{
    public partial class PendingReviewsPage : UserControl
    {
        public PendingReviewsPage()
        {
            InitializeComponent();
            var reviews = SourceGit.Rama.RamaServices.Reviews;
            reviews.EnsureStarted();
            DataContext = reviews;
        }
    }
}
