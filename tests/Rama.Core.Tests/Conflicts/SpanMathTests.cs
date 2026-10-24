using Rama.Core.Conflicts;

namespace Rama.Core.Tests.Conflicts;

public class SpanMathTests
{
    [Theory]
    [InlineData("a\nbb\nc\n", 2, 3, 2)]        // "bb\n" -> "bb"
    [InlineData("a\r\nbb\r\nc", 3, 4, 2)]      // "bb\r\n" -> "bb"
    [InlineData("a\nbb", 2, 2, 2)]             // sin salto final
    [InlineData("a\n\n", 2, 1, 0)]             // solo un salto
    [InlineData("ab", 0, 5, 5)]                // fuera de rango: sin cambios
    public void LengthWithoutTrailingBreak_ExcludesOnlyFinalBreak(string text, int start, int length, int expected) =>
        Assert.Equal(expected, SpanMath.LengthWithoutTrailingBreak(text, start, length));
}
