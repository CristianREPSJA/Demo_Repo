using System.Text.Json;
using Rama.Core.Http;

namespace Rama.Core.Jira;

public enum MoveState { Idle, Pending }

public enum MoveOutcome { NoOp, Committed, RequiresFields, NotAllowed, RolledBack, Busy }

public sealed record MoveResult(
    MoveOutcome Outcome,
    string? Message = null,
    IReadOnlyList<TransitionField>? MissingFields = null);

// Mueve un issue de columna: aplica local, llama a Jira y revierte si falla.
public sealed class OptimisticMove(IJiraTransitionApi api, BoardModel initial)
{
    public BoardModel Board { get; private set; } = initial;
    public MoveState State { get; private set; } = MoveState.Idle;
    public event Action<BoardModel>? BoardChanged;

    // Reemplaza el modelo (p. ej. refresco por polling) si no hay movimiento en curso.
    public void Refresh(BoardModel fresh)
    {
        if (State == MoveState.Pending) return;
        Apply(fresh);
    }

    public async Task<MoveResult> MoveAsync(string issueKey, JiraColumn target,
        IReadOnlyDictionary<string, JsonElement>? fields = null, CancellationToken ct = default)
    {
        if (State == MoveState.Pending)
            return new MoveResult(MoveOutcome.Busy, "Hay otro movimiento en curso.");
        var issue = Board.Find(issueKey);
        if (issue is null)
            return new MoveResult(MoveOutcome.RolledBack, $"No se encontró {issueKey} en el tablero.");
        if (target.StatusIds.Count == 0)
            return new MoveResult(MoveOutcome.NotAllowed, $"La columna {target.Name} no tiene estados.");
        if (target.StatusIds.Contains(issue.StatusId)) return new MoveResult(MoveOutcome.NoOp);

        State = MoveState.Pending;
        Apply(Board.WithIssue(issue with { StatusId = target.StatusIds[0] }));
        try
        {
            return await ExecuteAsync(issue, target, fields, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Rollback(issue);
            throw;
        }
        catch (Exception ex)
        {
            Rollback(issue);
            return new MoveResult(MoveOutcome.RolledBack, ErrorMessage(ex, issue, target));
        }
        finally
        {
            State = MoveState.Idle;
        }
    }

    private async Task<MoveResult> ExecuteAsync(JiraIssue issue, JiraColumn target,
        IReadOnlyDictionary<string, JsonElement>? fields, CancellationToken ct)
    {
        var transitions = await api.GetTransitionsAsync(issue.Key, ct).ConfigureAwait(false);
        var plan = TransitionPlanner.Plan(issue, target, transitions, fields?.Keys);
        if (plan.Kind == PlanKind.NotAllowed)
        {
            Rollback(issue);
            return new MoveResult(MoveOutcome.NotAllowed, plan.Reason);
        }
        if (plan.Kind == PlanKind.RequiresFields)
        {
            Rollback(issue);
            return new MoveResult(MoveOutcome.RequiresFields, MissingFields: plan.MissingFields);
        }

        var t = plan.Transition!;
        await api.TransitionAsync(issue.Key, t.Id, fields, ct).ConfigureAwait(false);
        Apply(Board.WithIssue(issue with { StatusId = t.ToStatusId, StatusName = t.ToStatusName }));
        return new MoveResult(MoveOutcome.Committed);
    }

    private void Rollback(JiraIssue original) => Apply(Board.WithIssue(original));

    private void Apply(BoardModel model)
    {
        Board = model;
        BoardChanged?.Invoke(model);
    }

    private static string ErrorMessage(Exception ex, JiraIssue issue, JiraColumn target) => ex switch
    {
        ApiAuthException => "Jira rechazó tus credenciales. Revisa tu API token.",
        ApiException { StatusCode: 0 } => "No se pudo conectar con Jira. Revisa tu conexión.",
        ApiException e => $"Jira rechazó mover {issue.Key} a {target.Name} ({(int)e.StatusCode}).",
        _ => $"No se pudo mover {issue.Key} a {target.Name}.",
    };
}
