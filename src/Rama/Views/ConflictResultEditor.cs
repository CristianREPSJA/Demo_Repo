using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

using Rama.Core.Conflicts;

namespace SourceGit.Views
{
    // RAMA: panel "Resultado" editable (F4b). Sincroniza el texto con ConflictResolver y pinta las regiones.
    public class ConflictResultEditor : TextEditor
    {
        protected override Type StyleKeyOverride => typeof(TextEditor);

        public ConflictResultEditor() : base(new TextArea(), new TextDocument())
        {
            ShowLineNumbers = true;
            WordWrap = false;
            Options.EnableHyperlinks = false;
            Options.EnableEmailHyperlinks = false;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            TextArea.LeftMargins[0].Margin = new Thickness(8, 0);
            TextArea.TextView.Margin = new Thickness(4, 0);
            TextArea.TextView.BackgroundRenderers.Add(new SpanRenderer(this));
            Document.Changed += OnDocumentChanged;
        }

        // Reemplaza el texto sin reenviar la edición al ViewModel.
        public void SetResult(string text, IReadOnlyList<ComposedSpan> spans)
        {
            _spans = spans ?? [];
            if (Text != text)
            {
                _syncing = true;
                try
                {
                    var caret = CaretOffset;
                    Document.Text = text ?? string.Empty;
                    CaretOffset = Math.Min(caret, Document.TextLength);
                }
                finally
                {
                    _syncing = false;
                }
            }

            TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }

        public void ScrollToOffset(int offset)
        {
            if (offset < 0 || offset > Document.TextLength)
                return;

            ScrollToLine(Document.GetLineByOffset(offset).LineNumber);
        }

        public event Action<int, int, string> ResultEdited;

        private void OnDocumentChanged(object sender, DocumentChangeEventArgs e)
        {
            if (!_syncing)
                ResultEdited?.Invoke(e.Offset, e.RemovalLength, Document.Text);
        }

        private sealed class SpanRenderer : IBackgroundRenderer
        {
            public KnownLayer Layer => KnownLayer.Background;

            public SpanRenderer(ConflictResultEditor owner) => _owner = owner;

            public void Draw(TextView textView, DrawingContext drawingContext)
            {
                var length = _owner.Document.TextLength;
                foreach (var span in _owner._spans)
                {
                    if (span.ConflictIndex < 0 || span.Length == 0 || span.Start + span.Length > length)
                        continue;

                    var brush = span.IsResolved ? ResolvedBrush : UnresolvedBrush;
                    var segment = new TextSegment { StartOffset = span.Start, Length = span.Length };
                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, true))
                        drawingContext.FillRectangle(brush, new Rect(0, rect.Y, textView.Bounds.Width, rect.Height));
                }
            }

            private static readonly IBrush ResolvedBrush = new SolidColorBrush(Color.FromArgb(40, 92, 197, 138));
            private static readonly IBrush UnresolvedBrush = new SolidColorBrush(Color.FromArgb(60, 230, 162, 60));
            private readonly ConflictResultEditor _owner;
        }

        private IReadOnlyList<ComposedSpan> _spans = [];
        private bool _syncing;
    }
}
