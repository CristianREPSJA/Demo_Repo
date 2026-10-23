namespace Rama.Core.Terminal;

// Envuelve la salida del PTY para que cada lectura termine en un límite de carácter UTF-8.
// Así un consumidor que decodifica cada bloque por separado no parte acentos ni box-drawing.
public sealed class Utf8BoundaryStream : Stream
{
    private const int MinBuffer = 4;
    private readonly Stream _inner;
    private readonly byte[] _pending = new byte[3];
    private int _pendingLength;

    public Utf8BoundaryStream(Stream inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadCore(buffer.AsMemory(offset, count), (m, _) => ValueTask.FromResult(_inner.Read(m.Span)), default)
            .AsTask().GetAwaiter().GetResult();

    public override int Read(Span<byte> buffer)
    {
        var tmp = new byte[buffer.Length];
        var n = Read(tmp, 0, tmp.Length);
        tmp.AsSpan(0, n).CopyTo(buffer);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ReadCore(buffer, (m, ct) => _inner.ReadAsync(m, ct), cancellationToken);

    private async ValueTask<int> ReadCore(Memory<byte> buffer, Func<Memory<byte>, CancellationToken, ValueTask<int>> read, CancellationToken ct)
    {
        if (buffer.Length < MinBuffer)
            return await FlushPendingOr(buffer, read, ct).ConfigureAwait(false);

        _pending.AsSpan(0, _pendingLength).CopyTo(buffer.Span);
        var total = _pendingLength;
        _pendingLength = 0;

        while (true)
        {
            var n = await read(buffer[total..], ct).ConfigureAwait(false);
            if (n == 0)
                return total; // fin del flujo: se entrega lo retenido tal cual

            total += n;
            var tail = Utf8Boundary.IncompleteTailLength(buffer.Span[..total]);
            // Si todo lo leído es un inicio incompleto (tail == total) se sigue leyendo; cabe porque tail <= 3 < buffer.
            if (tail < total)
            {
                buffer.Span.Slice(total - tail, tail).CopyTo(_pending);
                _pendingLength = tail;
                return total - tail;
            }
        }
    }

    // Búfer demasiado pequeño para alinear: se entregan primero los bytes retenidos.
    private async ValueTask<int> FlushPendingOr(Memory<byte> buffer, Func<Memory<byte>, CancellationToken, ValueTask<int>> read, CancellationToken ct)
    {
        if (_pendingLength == 0)
            return await read(buffer, ct).ConfigureAwait(false);

        var n = Math.Min(buffer.Length, _pendingLength);
        _pending.AsSpan(0, n).CopyTo(buffer.Span);
        _pending.AsSpan(n, _pendingLength - n).CopyTo(_pending);
        _pendingLength -= n;
        return n;
    }

    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.WriteAsync(buffer, offset, count, cancellationToken);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(buffer, cancellationToken);
    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
