namespace Rama.Core.Terminal;

public enum ContextAction
{
    ExplainChanges,
    CommitMessage,
    ReviewBranch,
    SummarizeTicket,
}

// Elige el pedido según el contexto; devuelve null si la acción no aplica.
public static class ContextPrompts
{
    public static bool IsAvailable(ContextAction action, TerminalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return action switch
        {
            ContextAction.ExplainChanges => context.SelectedFiles.Any(f => !string.IsNullOrWhiteSpace(f)),
            ContextAction.CommitMessage => true,
            ContextAction.ReviewBranch => !string.IsNullOrWhiteSpace(context.BaseBranch),
            ContextAction.SummarizeTicket => !string.IsNullOrWhiteSpace(context.TicketKey),
            _ => false,
        };
    }

    public static string? Build(ContextAction action, TerminalContext context)
    {
        if (!IsAvailable(action, context))
            return null;
        return action switch
        {
            ContextAction.ExplainChanges => PromptBuilder.ExplainChanges(context.SelectedFiles),
            ContextAction.CommitMessage => PromptBuilder.CommitMessage(context.TicketKey, context.StagedFiles),
            ContextAction.ReviewBranch => PromptBuilder.ReviewBranch(context.BaseBranch!),
            ContextAction.SummarizeTicket => PromptBuilder.SummarizeTicket(context.TicketKey!),
            _ => null,
        };
    }
}
