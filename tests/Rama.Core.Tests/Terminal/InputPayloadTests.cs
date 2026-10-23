using Rama.Core.Terminal;

namespace Rama.Core.Tests.Terminal;

public class InputPayloadTests
{
    [Fact]
    public void Build_Plain_NoEnter()
    {
        Assert.Equal("echo hola", InputPayload.Build("echo hola", bracketedPaste: false, submit: false));
    }

    [Fact]
    public void Build_Bracketed_WrapsText()
    {
        Assert.Equal("\u001b[200~hola\u001b[201~", InputPayload.Build("hola", bracketedPaste: true, submit: false));
    }

    [Fact]
    public void Build_Submit_AppendsCarriageReturnOutsidePaste()
    {
        Assert.Equal("\u001b[200~x\u001b[201~\r", InputPayload.Build("x", bracketedPaste: true, submit: true));
        Assert.Equal("x\r", InputPayload.Build("x", bracketedPaste: false, submit: true));
    }

    [Fact]
    public void Build_StripsEmbeddedNewlinesAndEscapes()
    {
        var payload = InputPayload.Build("a\r\nb\u001b[201~c", bracketedPaste: true, submit: false);
        Assert.Equal("\u001b[200~a b[201~c\u001b[201~", payload);
        Assert.DoesNotContain('\r', payload);
    }

    [Fact]
    public void Build_EmptyText_ReturnsEmptyUnlessSubmit()
    {
        Assert.Equal(string.Empty, InputPayload.Build("", bracketedPaste: true, submit: false));
        Assert.Equal("\r", InputPayload.Build(null, bracketedPaste: true, submit: true));
    }
}
