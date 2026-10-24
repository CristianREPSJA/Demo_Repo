using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    // Tablero Jira: el arrastre de tarjetas usa Avalonia DragDrop; la logica vive en JiraBoardViewModel.
    public partial class JiraBoardPage : UserControl
    {
        private static readonly DataFormat<string> DndIssue =
            DataFormat.CreateStringApplicationFormat("rama-jira-issue");

        public JiraBoardPage()
        {
            InitializeComponent();
            DataContext = new ViewModels.JiraBoardViewModel();
        }

        private ViewModels.JiraBoardViewModel Vm => (ViewModels.JiraBoardViewModel)DataContext;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _watch = PageVisibility.Watch(this, visible => Vm.SetVisible(visible));
            Vm.SetVisible(PageVisibility.IsShown(this));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _watch?.Dispose();
            _watch = null;
            Vm.SetVisible(false);
        }

        private void OnCardPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                e.Source is not Button && !IsInsideButton(e.Source))
            {
                _pressed = e;
                _dragging = false;
            }
        }

        private void OnCardReleased(object sender, PointerReleasedEventArgs e)
        {
            _pressed = null;
            _dragging = false;
        }

        private async void OnCardMoved(object sender, PointerEventArgs e)
        {
            if (_pressed == null || _dragging || sender is not Border { DataContext: ViewModels.JiraCardViewModel card } border)
                return;

            var delta = e.GetPosition(border) - _pressed.GetPosition(border);
            if (delta.X * delta.X + delta.Y * delta.Y < 64)
                return;

            _dragging = true;
            var start = _pressed;
            _pressed = null;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DndIssue, card.Key));
            _ = Vm.BeginDragAsync(card);
            try
            {
                await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move);
            }
            finally
            {
                Vm.EndDrag(card);
                _dragging = false;
            }
        }

        private void OnColumnDragOver(object sender, DragEventArgs e)
        {
            var ok = e.DataTransfer.Contains(DndIssue) &&
                sender is Border { DataContext: ViewModels.JiraColumnViewModel col } &&
                col.DropState != ViewModels.JiraDropState.Invalid;
            e.DragEffects = ok ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private async void OnColumnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (sender is not Border { DataContext: ViewModels.JiraColumnViewModel col })
                return;

            var key = e.DataTransfer.TryGetValue(DndIssue);
            if (!string.IsNullOrEmpty(key))
                await Vm.DropAsync(key, col);
        }

        private static bool IsInsideButton(object source)
        {
            for (var v = source as Visual; v != null; v = v.GetVisualParent())
                if (v is Button)
                    return true;
            return false;
        }

        private IDisposable _watch;
        private PointerPressedEventArgs _pressed;
        private bool _dragging;
    }
}
