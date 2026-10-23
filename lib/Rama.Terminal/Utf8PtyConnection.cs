using Porta.Pty;
using Rama.Core.Terminal;

namespace Rama.Terminal;

// Conexión PTY cuya salida se entrega alineada a caracteres UTF-8.
// La librería decodifica cada lectura con Encoding.UTF8.GetString sin estado; esto evita
// que un acento o un box-drawing partido entre lecturas se vea como U+FFFD.
public sealed class Utf8PtyConnection : IPtyConnection
{
    private readonly IPtyConnection _inner;
    private readonly Utf8BoundaryStream _reader;

    public Utf8PtyConnection(IPtyConnection inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _reader = new Utf8BoundaryStream(inner.ReaderStream);
    }

    public Stream ReaderStream => _reader;
    public Stream WriterStream => _inner.WriterStream;
    public int Pid => _inner.Pid;
    public int ExitCode => _inner.ExitCode;

    public event EventHandler<PtyExitedEventArgs>? ProcessExited
    {
        add => _inner.ProcessExited += value;
        remove => _inner.ProcessExited -= value;
    }

    public bool WaitForExit(int milliseconds) => _inner.WaitForExit(milliseconds);
    public void Kill() => _inner.Kill();
    public void Resize(int cols, int rows) => _inner.Resize(cols, rows);

    // El flujo interno lo libera la conexión original.
    public void Dispose() => _inner.Dispose();
}
