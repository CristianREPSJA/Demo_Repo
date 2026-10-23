using System;
using System.ComponentModel;
using Avalonia.Controls;
using VmLauncher = SourceGit.ViewModels.Launcher;
using VmPage = SourceGit.ViewModels.LauncherPage;
using VmRepository = SourceGit.ViewModels.Repository;

namespace SourceGit.Views
{
    // Pagina Terminal del riel: aloja el panel del repositorio de la pestaña activa.
    public partial class TerminalPage : UserControl
    {
        public TerminalPage()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Unwatch();
            _launcher = DataContext as VmLauncher;
            if (_launcher != null)
                ((INotifyPropertyChanged)_launcher).PropertyChanged += OnLauncherChanged;
            WatchPage(_launcher?.ActivePage);
            Sync();
        }

        private void OnLauncherChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(VmLauncher.ActivePage))
                return;
            WatchPage(_launcher.ActivePage);
            Sync();
        }

        private void OnPageChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VmPage.Data))
                Sync();
        }

        private void WatchPage(VmPage page)
        {
            if (_page != null)
                ((INotifyPropertyChanged)_page).PropertyChanged -= OnPageChanged;
            _page = page;
            if (_page != null)
                ((INotifyPropertyChanged)_page).PropertyChanged += OnPageChanged;
        }

        private void Unwatch()
        {
            if (_launcher != null)
                ((INotifyPropertyChanged)_launcher).PropertyChanged -= OnLauncherChanged;
            WatchPage(null);
        }

        private void Sync()
        {
            var repo = _launcher?.ActivePage?.Data as VmRepository;
            Host.Repository = repo;
            EmptyMessage.IsVisible = repo == null;
        }

        private VmLauncher _launcher;
        private VmPage _page;
    }
}
