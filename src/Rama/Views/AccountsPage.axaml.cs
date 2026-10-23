using Avalonia.Controls;

namespace SourceGit.Views
{
    public partial class AccountsPage : UserControl
    {
        public AccountsPage()
        {
            InitializeComponent();
            DataContext = new ViewModels.AccountsViewModel();
        }
    }
}
