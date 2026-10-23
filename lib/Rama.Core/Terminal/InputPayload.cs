namespace Rama.Core.Terminal;

// Bytes que se escriben en el PTY al insertar texto. Enter solo si submit = true.
public static class InputPayload
{
    public const string PasteStart = "\u001b[200~";
    public const string PasteEnd = "\u001b[201~";

    public static string Build(string? text, bool bracketedPaste, bool submit)
    {
        var clean = PromptBuilder.Sanitize(text);
        var body = clean.Length == 0 ? string.Empty : bracketedPaste ? PasteStart + clean + PasteEnd : clean;
        return submit ? body + "\r" : body;
    }
}
