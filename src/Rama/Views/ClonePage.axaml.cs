using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace SourceGit.Views
{
    public partial class ClonePage : UserControl
    {
        public ClonePage()
        {
            InitializeComponent();
            DataContext = new ViewModels.ClonePageViewModel();
        }

        // Al volver a mostrar la pagina se recargan cuentas y repos clonados.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && change.NewValue is true && DataContext is ViewModels.ClonePageViewModel vm)
                _ = vm.ReloadAsync();
        }

        private async void OnBrowse(object _, RoutedEventArgs e)
        {
            e.Handled = true;
            var top = TopLevel.GetTopLevel(this);
            if (top == null || DataContext is not ViewModels.ClonePageViewModel vm)
                return;

            try
            {
                var selected = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
                if (selected.Count != 1)
                    return;

                var folder = selected[0];
                var path = folder is { Path: { IsAbsoluteUri: true } uri } ? uri.LocalPath : folder.Path.ToString();
                vm.SetDestinationFromFolder(path.TrimEnd('\\', '/'));
            }
            catch (Exception ex)
            {
                Models.Notification.Send(null, ex.Message, true);
            }
        }
    }
}
