using System.Text;
using Rama.Core.Terminal;

namespace Rama.Core.Tests.Terminal;

public class Utf8BoundaryStreamTests
{
    // Entrega los bytes en los trozos indicados, como un PTY real.
    private sealed class ChunkedStream(params byte[][] chunks) : Stream
    {
        private int _index;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_index >= chunks.Length)
                return 0;
            var chunk = chunks[_index++];
            Array.Copy(chunk, 0, buffer, offset, chunk.Length);
            return chunk.Length;
        }
    }

    private static byte[][] Split(string text, params int[] cuts)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var parts = new List<byte[]>();
        var start = 0;
        foreach (var cut in cuts.Append(bytes.Length))
        {
            parts.Add(bytes[start..cut]);
            start = cut;
        }
        return [.. parts];
    }

    // Decodifica cada lectura por separado, igual que hace la librería del terminal.
    private static string ReadAllPerChunk(Stream stream, bool async = false)
    {
        var sb = new StringBuilder();
        var buffer = new byte[64];
        while (true)
        {
            var n = async ? stream.ReadAsync(buffer, 0, buffer.Length).GetAwaiter().GetResult() : stream.Read(buffer, 0, buffer.Length);
            if (n == 0)
                return sb.ToString();
            sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
        }
    }

    [Theory]
    [InlineData("ñ", 0, 0)]
    [InlineData("a", 0, 0)]
    [InlineData("añ", 2, 1)]
    [InlineData("a─", 2, 1)]
    [InlineData("a─", 3, 2)]
    [InlineData("a😀", 2, 1)]
    [InlineData("a😀", 4, 3)]
    [InlineData("a😀", 5, 0)]
    [InlineData("añ", 3, 0)]
    public void IncompleteTailLength_DetectsCutSequences(string text, int length, int expected)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var span = length == 0 ? bytes : bytes[..length];
        Assert.Equal(expected, Utf8Boundary.IncompleteTailLength(span));
    }

    [Fact]
    public void IncompleteTailLength_InvalidBytesAreNotHeld()
    {
        // Byte de continuación suelto: no es una secuencia esperando más datos.
        Assert.Equal(0, Utf8Boundary.IncompleteTailLength(new byte[] { 0x41, 0x80 }));
        Assert.Equal(0, Utf8Boundary.IncompleteTailLength(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Read_SplitAccentAcrossChunks_DecodesCorrectly()
    {
        const string text = "ñandú ─│┌┐";
        // Corte en medio de 'ñ' (2 bytes) y de '─' (3 bytes).
        using var stream = new Utf8BoundaryStream(new ChunkedStream(Split(text, 1, 9)));
        Assert.Equal(text, ReadAllPerChunk(stream));
    }

    [Fact]
    public void ReadAsync_SplitEmojiAcrossThreeChunks_DecodesCorrectly()
    {
        const string text = "x😀y";
        using var stream = new Utf8BoundaryStream(new ChunkedStream(Split(text, 2, 3)));
        Assert.Equal(text, ReadAllPerChunk(stream, async: true));
    }

    [Fact]
    public void Read_WithoutWrapper_ProducesReplacementChars()
    {
        // Documenta el defecto que corrige el wrapper.
        using var raw = new ChunkedStream(Split("ñ", 1));
        Assert.Contains('�', ReadAllPerChunk(raw));
    }

    [Fact]
    public void Read_IncompleteAtEndOfStream_FlushesPendingBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("añ")[..2];
        using var stream = new Utf8BoundaryStream(new ChunkedStream(bytes));
        var buffer = new byte[16];
        Assert.Equal(1, stream.Read(buffer, 0, buffer.Length));
        Assert.Equal(1, stream.Read(buffer, 0, buffer.Length));
        Assert.Equal(0xC3, buffer[0]);
        Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void Read_TinyBuffer_PassesThrough()
    {
        using var stream = new Utf8BoundaryStream(new MemoryStream(Encoding.UTF8.GetBytes("abc")));
        var buffer = new byte[2];
        Assert.Equal(2, stream.Read(buffer, 0, 2));
    }

    [Fact]
    public void Write_ForwardsToInnerStream()
    {
        var inner = new MemoryStream();
        using var stream = new Utf8BoundaryStream(inner);
        stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
        stream.Flush();
        Assert.Equal(new byte[] { 1, 2, 3 }, inner.ToArray());
        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.True(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Length);
        Assert.Throws<NotSupportedException>(() => stream.Position = 1);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
    }

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Utf8BoundaryStream(null!));
    }
}
