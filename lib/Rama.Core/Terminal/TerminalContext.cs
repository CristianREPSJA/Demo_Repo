namespace Rama.Core.Terminal;

// Contexto del repositorio que alimenta los botones de la terminal.
public sealed record TerminalContext(
    IReadOnlyList<string> SelectedFiles,
    IReadOnlyList<string> StagedFiles,
    string? TicketKey,
    string? BaseBranch)
{
    public static TerminalContext Empty { get; } = new([], [], null, null);
}
