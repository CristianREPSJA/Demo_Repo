using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using Rama.Core.Conflicts;

namespace SourceGit.ViewModels
{
    // RAMA: resolución de conflictos a 3 paneles (F4b). La lógica vive en Rama.Core.Conflicts.
    public class ConflictResolver : ObservableObject
    {
        public string FilePath => _ctx.FilePath;
        public string Banner => _ctx.Banner;
        public bool HasBanner => !string.IsNullOrEmpty(_ctx.Banner);
        public string OursTitle => App.Text("Rama.Conflict.Ours", _ctx.OursName);
        public string OursSha => _ctx.OursSha ?? string.Empty;
        public string TheirsTitle => App.Text("Rama.Conflict.Theirs", _ctx.TheirsName);
        public string TheirsSha => _ctx.TheirsSha ?? string.Empty;

        public List<ConflictBlock> Blocks
        {
            get => _blocks;
            private set => SetProperty(ref _blocks, value);
        }

        public string ResultText
        {
            get => _composed.Text;
        }

        public IReadOnlyList<ComposedSpan> ResultSpans
        {
            get => _composed.Spans;
        }

        public int ConflictCount
        {
            get => _document.ConflictCount;
        }

        public int UnresolvedCount
        {
            get => _unresolvedCount;
            private set => SetProperty(ref _unresolvedCount, value);
        }

        public int CurrentIndex
        {
            get => _currentIndex;
            private set
            {
                if (SetProperty(ref _currentIndex, value))
                    RefreshNavigation();
            }
        }

        public string NavigationText
        {
            get => _navigationText;
            private set => SetProperty(ref _navigationText, value);
        }

        public string ProgressText
        {
            get => _progressText;
            private set => SetProperty(ref _progressText, value);
        }

        public string Error
        {
            get => _error;
            private set => SetProperty(ref _error, value);
        }

        public bool IsDirty
        {
            get => _isDirty;
            private set => SetProperty(ref _isDirty, value);
        }

        public ConflictResolver(Repository repo, Models.Commit head, string filePath)
            : this(ConflictContext.FromRepository(repo, head, filePath))
        {
        }

        public ConflictResolver(ConflictContext ctx)
        {
            _ctx = ctx;

            var fullPath = Path.Combine(ctx.RepoPath, ctx.FilePath);
            var bytes = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : [];
            if (Array.IndexOf(bytes, (byte)0) >= 0)
            {
                _error = App.Text("Rama.Conflict.Binary");
                Load(string.Empty);
                return;
            }

            Load(Decode(bytes, out _encoding));
        }

        public void ToggleOurs(int index) => SetSelection(index, CurrentSelection(index).ToggleOurs());

        public void ToggleTheirs(int index) => SetSelection(index, CurrentSelection(index).ToggleTheirs());

        // Botón "Usar ambas (actual primero)" sobre el conflicto actual.
        public void UseBothOursFirst()
        {
            if (_currentIndex >= 0)
                SetSelection(_currentIndex, new RegionSelection(true, true, true));
        }

        public void GotoPrev()
        {
            if (_currentIndex > 0)
                CurrentIndex = _currentIndex - 1;
        }

        public void GotoNext()
        {
            if (_currentIndex < ConflictCount - 1)
                CurrentIndex = _currentIndex + 1;
        }

        // Edición del panel Resultado: offset/longitud eliminada y el texto completo nuevo.
        public void ApplyResultEdit(int offset, int removedLength, string newText)
        {
            if (newText == _composed.Text)
                return;

            var outcome = ResultEdit.Apply(_document, _choices, _composed, offset, removedLength, newText);
            var reparsed = outcome.Reparsed;
            _document = outcome.Document;
            _choices = [.. outcome.Choices];
            if (reparsed)
                _selections = NewSelections(_document.ConflictCount);

            for (var i = 0; i < _choices.Length; i++)
            {
                if (_choices[i].Kind == ResolutionKind.Manual)
                    _selections[i] = RegionSelection.None;
            }

            IsDirty = true;
            if (reparsed)
            {
                Blocks = BuildBlocks(_document);
                _currentIndex = Math.Min(_currentIndex, _document.ConflictCount - 1);
            }

            Recompose();
        }

        public async Task<bool> SaveAndStageAsync()
        {
            var text = _composed.Text;
            var remaining = Math.Max(_composed.UnresolvedCount, ConflictParser.Parse(text).ConflictCount);
            if (remaining > 0)
            {
                Error = App.Text("Rama.Conflict.Remaining", remaining);
                return false;
            }

            var error = await MergeConflictEditor.WriteAndStageAsync(_ctx.RepoPath, _ctx.FilePath, text, _encoding);
            if (error != null)
            {
                Error = App.Text("Rama.Conflict.SaveFailed", error);
                return false;
            }

            _ctx.AfterStaged?.Invoke();
            IsDirty = false;
            return true;
        }

        public void ClearErrorMessage()
        {
            Error = string.Empty;
        }

        private void Load(string text)
        {
            _document = ConflictParser.Parse(text);
            _choices = new RegionResolution[_document.ConflictCount];
            Array.Fill(_choices, RegionResolution.Unresolved);
            _selections = NewSelections(_document.ConflictCount);
            _blocks = BuildBlocks(_document);
            _currentIndex = _document.ConflictCount > 0 ? 0 : -1;
            Recompose();
        }

        private RegionSelection CurrentSelection(int index) =>
            _choices[index].Kind == ResolutionKind.Manual ? RegionSelection.None : _selections[index];

        private void SetSelection(int index, RegionSelection selection)
        {
            if (index < 0 || index >= _choices.Length)
                return;

            _selections[index] = selection;
            _choices[index] = selection.ToResolution();
            IsDirty = true;
            CurrentIndex = index;
            Recompose();
        }

        private void Recompose()
        {
            _composed = ResolutionComposer.Compose(_document, _choices);
            OnPropertyChanged(nameof(ResultSpans));
            OnPropertyChanged(nameof(ResultText));
            UnresolvedCount = _composed.UnresolvedCount;

            foreach (var block in _blocks)
            {
                if (!block.IsConflict || block.ConflictIndex >= _choices.Length)
                    continue;

                var i = block.ConflictIndex;
                var manual = _choices[i].Kind == ResolutionKind.Manual;
                var resolved = _composed.Spans[block.SegmentIndex].IsResolved;
                block.SetState(_selections[i], manual, resolved, i == _currentIndex);
            }

            RefreshNavigation();
        }

        private void RefreshNavigation()
        {
            var total = ConflictCount;
            NavigationText = total == 0
                ? App.Text("Rama.Conflict.NavNone")
                : App.Text("Rama.Conflict.Nav", _currentIndex + 1, total);
            ProgressText = App.Text("Rama.Conflict.Progress", total - _unresolvedCount, total);

            foreach (var block in _blocks)
                block.IsCurrent = block.IsConflict && block.ConflictIndex == _currentIndex;
        }

        private List<ConflictBlock> BuildBlocks(ConflictDocument doc)
        {
            var blocks = new List<ConflictBlock>(doc.Segments.Count);
            var oursLine = 1;
            var theirsLine = 1;
            for (var s = 0; s < doc.Segments.Count; s++)
            {
                var (ours, theirs, index) = doc.Segments[s] is ConflictHunk h
                    ? (h.Ours, h.Theirs, h.Index)
                    : (doc.Segments[s].OriginalText, doc.Segments[s].OriginalText, -1);

                var block = new ConflictBlock(this, s, index, ours, theirs, oursLine, theirsLine);
                oursLine += ConflictBlock.CountLines(ours);
                theirsLine += ConflictBlock.CountLines(theirs);
                blocks.Add(block);
            }
            return blocks;
        }

        private static RegionSelection[] NewSelections(int count)
        {
            var selections = new RegionSelection[count];
            Array.Fill(selections, RegionSelection.None);
            return selections;
        }

        // Respeta BOM UTF-8; si no es UTF-8 válido usa Latin1 para no alterar bytes al guardar.
        private static string Decode(byte[] bytes, out Encoding encoding)
        {
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var offset = hasBom ? 3 : 0;
            try
            {
                encoding = new UTF8Encoding(hasBom, true);
                return encoding.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException)
            {
                encoding = Encoding.Latin1;
                return encoding.GetString(bytes);
            }
        }

        private readonly ConflictContext _ctx;
        private readonly Encoding _encoding = new UTF8Encoding(false);
        private ConflictDocument _document;
        private RegionResolution[] _choices = [];
        private RegionSelection[] _selections = [];
        private ComposeResult _composed;
        private List<ConflictBlock> _blocks = [];
        private int _unresolvedCount;
        private int _currentIndex = -1;
        private string _navigationText = string.Empty;
        private string _progressText = string.Empty;
        private string _error = string.Empty;
        private bool _isDirty;
    }
}
