using System;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using Rama.Core.Conflicts;

namespace SourceGit.ViewModels
{
    // RAMA: bloque de los paneles Actual/Entrante: texto común o una región en conflicto con sus casillas.
    public class ConflictBlock : ObservableObject
    {
        public const double LineHeight = 20;

        public int SegmentIndex { get; }
        public int ConflictIndex { get; }
        public bool IsConflict => ConflictIndex >= 0;
        public string OursText { get; }
        public string TheirsText { get; }
        public string OursLineNumbers { get; }
        public string TheirsLineNumbers { get; }
        public double Height { get; }

        public bool IsOursChecked
        {
            get => _isOursChecked;
            set
            {
                if (value != _isOursChecked)
                    _owner.ToggleOurs(ConflictIndex);
            }
        }

        public bool IsTheirsChecked
        {
            get => _isTheirsChecked;
            set
            {
                if (value != _isTheirsChecked)
                    _owner.ToggleTheirs(ConflictIndex);
            }
        }

        public bool IsManual
        {
            get => _isManual;
            private set => SetProperty(ref _isManual, value);
        }

        public bool IsResolved
        {
            get => _isResolved;
            private set => SetProperty(ref _isResolved, value);
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetProperty(ref _isCurrent, value);
        }

        internal ConflictBlock(ConflictResolver owner, int segmentIndex, int conflictIndex, string ours, string theirs, int oursStart, int theirsStart)
        {
            _owner = owner;
            SegmentIndex = segmentIndex;
            ConflictIndex = conflictIndex;
            OursText = ToDisplay(ours);
            TheirsText = ToDisplay(theirs);

            var oursCount = CountLines(ours);
            var theirsCount = CountLines(theirs);
            OursLineNumbers = Numbers(oursStart, oursCount);
            TheirsLineNumbers = Numbers(theirsStart, theirsCount);
            Height = Math.Max(1, Math.Max(oursCount, theirsCount)) * LineHeight;
        }

        internal void SetState(RegionSelection selection, bool manual, bool resolved, bool current)
        {
            SetProperty(ref _isOursChecked, selection.Ours, nameof(IsOursChecked));
            SetProperty(ref _isTheirsChecked, selection.Theirs, nameof(IsTheirsChecked));
            IsManual = manual;
            IsResolved = resolved;
            IsCurrent = current;
        }

        internal static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var count = text.Count(c => c == '\n');
            return text.EndsWith('\n') ? count : count + 1;
        }

        private static string ToDisplay(string text)
        {
            var display = text.Replace("\r", string.Empty, StringComparison.Ordinal);
            return display.EndsWith('\n') ? display[..^1] : display;
        }

        private static string Numbers(int start, int count) =>
            string.Join('\n', Enumerable.Range(start, count));

        private readonly ConflictResolver _owner;
        private bool _isOursChecked;
        private bool _isTheirsChecked;
        private bool _isManual;
        private bool _isResolved;
        private bool _isCurrent;
    }
}
