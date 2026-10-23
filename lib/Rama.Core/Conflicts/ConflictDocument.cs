using System.Text;

namespace Rama.Core.Conflicts;

// Segmento de un archivo con conflictos: texto común o región en conflicto. Guarda el texto crudo (con fines de línea).
public abstract record DocumentSegment
{
    public abstract string OriginalText { get; }
}

public sealed record CommonSegment(string Text) : DocumentSegment
{
    public override string OriginalText => Text;
}

// Región <<<<<<< / ||||||| / ======= / >>>>>>>; cada marcador y lado incluye sus fines de línea.
public sealed record ConflictHunk(
    int Index,
    string StartMarker,
    string Ours,
    string? BaseMarker,
    string? Base,
    string Separator,
    string Theirs,
    string EndMarker) : DocumentSegment
{
    public bool HasBase => BaseMarker is not null;
    public string OursLabel => LabelOf(StartMarker);
    public string TheirsLabel => LabelOf(EndMarker);

    // El marcador final sin salto de línea implica que la región termina el archivo.
    public bool EndsAtEofWithoutNewline => !EndMarker.EndsWith('\n');

    public override string OriginalText =>
        string.Concat(StartMarker, Ours, BaseMarker ?? string.Empty, Base ?? string.Empty, Separator, Theirs, EndMarker);

    private static string LabelOf(string marker) => marker.Length > 8 ? marker[8..].TrimEnd('\r', '\n') : string.Empty;
}

public sealed class ConflictDocument
{
    public IReadOnlyList<DocumentSegment> Segments { get; }
    public IReadOnlyList<ConflictHunk> Hunks { get; }
    public int ConflictCount => Hunks.Count;

    public string OriginalText
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var s in Segments)
                sb.Append(s.OriginalText);
            return sb.ToString();
        }
    }

    public ConflictDocument(IReadOnlyList<DocumentSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = segments.ToArray();
        Hunks = Segments.OfType<ConflictHunk>().ToArray();
    }

    // Devuelve un documento nuevo con el texto común del segmento reemplazado.
    public ConflictDocument WithCommonText(int segmentIndex, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(segmentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(segmentIndex, Segments.Count);
        if (Segments[segmentIndex] is not CommonSegment)
            throw new ArgumentException("El segmento no es texto común.", nameof(segmentIndex));

        var copy = Segments.ToArray();
        copy[segmentIndex] = new CommonSegment(text);
        return new ConflictDocument(copy);
    }
}
