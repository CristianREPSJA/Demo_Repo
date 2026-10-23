using Avalonia;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SourceGit.ViewModels;

namespace SourceGit.Rama.Terminal
{
    // Aloja la vista del terminal de un repositorio. Solo muestra el panel mientras es visible
    // y, si otro alojador lo reclama, cede la vista (una sola vista a la vez).
    public class TerminalHost : ContentControl
    {
        public static readonly StyledProperty<Repository> RepositoryProperty =
            AvaloniaProperty.Register<TerminalHost, Repository>(nameof(Repository));

        public Repository Repository
        {
            get => GetValue(RepositoryProperty);
            set => SetValue(RepositoryProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == RepositoryProperty || change.Property == IsVisibleProperty)
                Sync();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            WatchAncestors();
            Sync();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            UnwatchAncestors();
            ReleaseCurrent();
        }

        private void Sync()
        {
            var repo = Repository;
            if (repo == null || !IsEffectivelyVisible || VisualRoot == null)
            {
                ReleaseCurrent();
                return;
            }

            var entry = TerminalRegistry.Instance.Acquire(repo);
            if (ReferenceEquals(_entry, entry))
                return;

            ReleaseCurrent();
            _entry = entry;
            entry.Panel.Lease.Acquire(this, Revoke); // desaloja al alojador anterior antes de mudar la vista
            Content = entry.View;
        }

        // Avalonia no expone un evento de visibilidad efectiva: se observa IsVisible de cada ancestro.
        private void WatchAncestors()
        {
            UnwatchAncestors();
            foreach (var ancestor in this.GetVisualAncestors())
            {
                ancestor.PropertyChanged += OnAncestorChanged;
                _watched.Add(ancestor);
            }
        }

        private void UnwatchAncestors()
        {
            foreach (var ancestor in _watched)
                ancestor.PropertyChanged -= OnAncestorChanged;
            _watched.Clear();
        }

        private void OnAncestorChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty)
                Sync();
        }

        private void Revoke()
        {
            Content = null;
            _entry = null;
        }

        private void ReleaseCurrent()
        {
            if (_entry != null && _entry.Panel.Lease.Release(this))
                Content = null;
            _entry = null;
        }

        private readonly List<Visual> _watched = [];
        private TerminalEntry _entry;
    }
}
