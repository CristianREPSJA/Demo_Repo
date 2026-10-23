using System.Text;
using Porta.Pty;

namespace Rama.Terminal.Tests;

public class TerminalBridgeContractTests
{
    [Fact]
    public void BindingError_IsNull_ForPinnedLibraryVersion()
    {
        // Si falla tras actualizar Iciclecreek.Avalonia.Terminal, revisar TerminalBridge.
        Assert.Null(TerminalBridge.BindingError);
    }

    private sealed class FakeConnection : IPtyConnection
    {
        public Stream ReaderStream { get; } = new MemoryStream(Encoding.UTF8.GetBytes("ñ"));
        public Stream WriterStream { get; } = new MemoryStream();
        public int Pid => 7;
        public int ExitCode => 3;
        public int Kills;
        public int Disposals;
        public (int, int) LastResize;
        public EventHandler<PtyExitedEventArgs>? Handlers;
        public event EventHandler<PtyExitedEventArgs>? ProcessExited { add => Handlers += value; remove => Handlers -= value; }
        public bool WaitForExit(int milliseconds) => milliseconds > 0;
        public void Kill() => Kills++;
        public void Resize(int cols, int rows) => LastResize = (cols, rows);
        public void Dispose() => Disposals++;
    }

    [Fact]
    public void Utf8PtyConnection_DelegatesToInner()
    {
        var inner = new FakeConnection();
        var wrapped = new Utf8PtyConnection(inner);
        EventHandler<PtyExitedEventArgs> handler = (_, _) => { };

        wrapped.ProcessExited += handler;
        Assert.NotNull(inner.Handlers);
        wrapped.ProcessExited -= handler;
        Assert.Null(inner.Handlers);

        wrapped.Kill();
        wrapped.Resize(80, 24);
        wrapped.Dispose();
        Assert.Equal(1, inner.Kills);
        Assert.Equal((80, 24), inner.LastResize);
        Assert.Equal(1, inner.Disposals);
        Assert.True(wrapped.WaitForExit(1));
        Assert.Equal(7, wrapped.Pid);
        Assert.Equal(3, wrapped.ExitCode);
        Assert.Same(inner.WriterStream, wrapped.WriterStream);
    }

    [Fact]
    public void Utf8PtyConnection_ReaderIsUtf8Aligned()
    {
        var wrapped = new Utf8PtyConnection(new FakeConnection());
        Assert.IsType<Rama.Core.Terminal.Utf8BoundaryStream>(wrapped.ReaderStream);
        var buffer = new byte[8];
        Assert.Equal(2, wrapped.ReaderStream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void Utf8PtyConnection_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Utf8PtyConnection(null!));
    }
}
