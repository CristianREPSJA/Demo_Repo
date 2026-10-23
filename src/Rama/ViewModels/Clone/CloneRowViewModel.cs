using System;

using CommunityToolkit.Mvvm.ComponentModel;

using Rama.Core.Clone;

namespace SourceGit.ViewModels
{
    // Fila de la tabla de repos remotos; LocalPath != null significa "Ya clonado".
    public partial class CloneRowViewModel : ObservableObject
    {
        public CloneRepoItem Item { get; }
        public string Name => Item.Name;
        public string Description => Item.Description ?? string.Empty;
        public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Description);
        public string Project => Item.Project;
        public string Branch => string.IsNullOrEmpty(Item.DefaultBranch) ? CloneListLogic.Unknown : Item.DefaultBranch;
        public string UpdatedText { get; }
        public string SizeText { get; }

        [ObservableProperty]
        private string _localPath;

        public bool IsCloned => !string.IsNullOrEmpty(LocalPath);

        public CloneRowViewModel(CloneRepoItem item, string localPath, DateTimeOffset now)
        {
            Item = item;
            _localPath = localPath;
            UpdatedText = CloneListLogic.FormatUpdated(item.UpdatedOn, now);
            SizeText = CloneListLogic.FormatSize(item.SizeBytes);
        }

        partial void OnLocalPathChanged(string value) => OnPropertyChanged(nameof(IsCloned));
    }

    // Tarjeta de cuenta conectada (columna izquierda).
    public sealed record CloneAccountCard(string Platform, string Detail, string ExpiryText, bool IsMono)
    {
        public bool HasExpiry => !string.IsNullOrEmpty(ExpiryText);
    }
}
