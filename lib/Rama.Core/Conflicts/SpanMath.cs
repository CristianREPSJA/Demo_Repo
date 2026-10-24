namespace Rama.Core.Conflicts;

// Cálculos de tramos del editor de resultado (sin UI).
public static class SpanMath
{
    // Largo del tramo sin el salto de línea final, para resaltar solo las líneas de la región.
    public static int LengthWithoutTrailingBreak(string text, int start, int length)
    {
        if (start < 0 || length <= 0 || start + length > text.Length)
            return Math.Max(length, 0);

        var end = start + length;
        if (text[end - 1] == '\n')
            end--;
        if (end > start && text[end - 1] == '\r')
            end--;
        return end - start;
    }
}
