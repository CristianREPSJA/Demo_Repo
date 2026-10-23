using System;
using System.ComponentModel;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    // RAMA: ventana del resolvedor de conflictos (F4b).
    public partial class ConflictResolver : ChromelessWindow
    {
        public ConflictResolver()
        {
            InitializeComponent();
            ResultEditor.ResultEdited += OnResultEdited;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_vm != null)
                _vm.PropertyChanged -= OnViewModelPropertyChanged;

            _vm = DataContext as ViewModels.ConflictResolver;
            if (_vm == null)
                return;

            _vm.PropertyChanged += OnViewModelPropertyChanged;
            ResultEditor.SetResult(_vm.ResultText, _vm.ResultSpans);
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (_forceClose || _vm is not { IsDirty: true })
                return;

            e.Cancel = true;
            var confirm = new Confirm();
            confirm.SetData(App.Text("MergeConflictEditor.UnsavedChanges"), Models.ConfirmButtonType.YesNo);
            if (await confirm.ShowDialog<bool>(this))
            {
                _forceClose = true;
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_vm != null)
                _vm.PropertyChanged -= OnViewModelPropertyChanged;

            base.OnClosed(e);
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.ConflictResolver.ResultText))
                ResultEditor.SetResult(_vm.ResultText, _vm.ResultSpans);
            else if (e.PropertyName == nameof(ViewModels.ConflictResolver.CurrentIndex))
                RevealCurrent();
        }

        private void OnResultEdited(int offset, int removedLength, string text)
        {
            _vm?.ApplyResultEdit(offset, removedLength, text);
        }

        private void OnGotoPrev(object sender, RoutedEventArgs e)
        {
            _vm?.GotoPrev();
            e.Handled = true;
        }

        private void OnGotoNext(object sender, RoutedEventArgs e)
        {
            _vm?.GotoNext();
            e.Handled = true;
        }

        private void OnUseBoth(object sender, RoutedEventArgs e)
        {
            _vm?.UseBothOursFirst();
            e.Handled = true;
        }

        private async void OnMarkResolved(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_vm != null && await _vm.SaveAndStageAsync())
            {
                _forceClose = true;
                Close();
            }
        }

        // Lleva la región actual a la vista en los tres paneles.
        private void RevealCurrent()
        {
            if (_vm == null || _vm.CurrentIndex < 0)
                return;

            var blockIndex = _vm.Blocks.FindIndex(b => b.ConflictIndex == _vm.CurrentIndex);
            if (blockIndex >= 0)
            {
                OursItems.ContainerFromIndex(blockIndex)?.BringIntoView();
                TheirsItems.ContainerFromIndex(blockIndex)?.BringIntoView();
            }

            var span = _vm.ResultSpans.FirstOrDefault(s => s.ConflictIndex == _vm.CurrentIndex);
            if (span != null)
                ResultEditor.ScrollToOffset(span.Start);
        }

        private ViewModels.ConflictResolver _vm;
        private bool _forceClose;
    }
}
