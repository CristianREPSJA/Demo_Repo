using System.Globalization;
using System.Text;

namespace Rama.Core.Terminal;

// Pedidos de los botones de contexto. Solo texto de una línea: nunca saltos, ESC ni controles.
public static class PromptBuilder
{
    public const int MaxFiles = 20;

    public static string ExplainChanges(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var clean = files.Select(Sanitize).Where(f => f.Length > 0).ToList();
        if (clean.Count == 0)
            throw new ArgumentException("Se requiere al menos un archivo.", nameof(files));

        return Sanitize($"Explica los cambios seleccionados en: {FileList(clean)}");
    }

    public static string CommitMessage(string? ticketKey) => CommitMessage(ticketKey, null);

    // Con archivos preparados los nombra en el pedido; sin ellos queda la forma genérica.
    public static string CommitMessage(string? ticketKey, IEnumerable<string>? stagedFiles)
    {
        var ticket = Sanitize(ticketKey);
        var format = ticket.Length > 0 ? $"{ticket} <resumen>" : "<resumen>";
        var clean = (stagedFiles ?? []).Select(Sanitize).Where(f => f.Length > 0).ToList();
        var staged = clean.Count > 0 ? $"git diff --staged: {FileList(clean)}" : "git diff --staged";
        return Sanitize($"Redacta un mensaje de commit en español para los cambios preparados ({staged}), formato: {format}");
    }

    private static string FileList(List<string> clean)
    {
        var shown = string.Join(", ", clean.Take(MaxFiles));
        var extra = clean.Count - MaxFiles;
        return extra > 0 ? $"{shown} y {extra} más" : shown;
    }

    public static string ReviewBranch(string baseBranch) =>
        Sanitize($"Revisa la rama actual contra {Required(baseBranch, nameof(baseBranch))} y señala riesgos");

    public static string SummarizeTicket(string ticketKey) =>
        Sanitize($"Resume el ticket {Required(ticketKey, nameof(ticketKey))} y su estado");

    // CR/LF/TAB pasan a espacio; ESC, C0/C1 y caracteres de formato (p. ej. U+202E) se eliminan.
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n' or '\t')
            {
                if (!lastWasSpace)
                    sb.Append(' ');
                lastWasSpace = true;
                continue;
            }
            if (char.IsControl(ch) || CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format)
                continue;
            sb.Append(ch);
            lastWasSpace = ch == ' ';
        }
        return sb.ToString().Trim();
    }

    private static string Required(string? value, string name)
    {
        var clean = Sanitize(value);
        if (clean.Length == 0)
            throw new ArgumentException("Valor requerido.", name);
        return clean;
    }
}
