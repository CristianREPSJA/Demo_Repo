using Rama.Core.Azure;

namespace Rama.Core.Reviews;

// Mapeo puro de modelos de Azure a filas de la pantalla (agrupación, etiquetas, tonos).
public static class ReviewMapper
{
    public static readonly IReadOnlyList<VoteOption> VoteOptions =
    [
        new(Vote.Approved, "Aprobar", false),
        new(Vote.ApprovedWithSuggestions, "Aprobar con sugerencias", false),
        new(Vote.WaitingForAuthor, "Esperar al autor", false),
        new(Vote.Rejected, "Rechazar", true),
    ];

    public static string BranchName(string? refName) =>
        (refName ?? "").StartsWith("refs/heads/", StringComparison.Ordinal) ? refName![11..] : refName ?? "";

    public static string CommitsText(int n) => n == 1 ? "1 commit" : $"{n} commits";

    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    public static (string Text, Tone Tone) VoteBadge(Vote v) => v switch
    {
        Vote.Approved or Vote.ApprovedWithSuggestions => (v.Label(), Tone.Good),
        Vote.WaitingForAuthor => (v.Label(), Tone.Warn),
        Vote.Rejected => (v.Label(), Tone.Bad),
        _ => (v.Label(), Tone.Neutral),
    };

    public static (string Text, Tone Tone) PolicyBadge(PolicyStatus s) => s switch
    {
        PolicyStatus.Approved => ("Cumplida", Tone.Good),
        PolicyStatus.Rejected => ("Rechazada", Tone.Bad),
        PolicyStatus.Broken => ("Con error", Tone.Bad),
        PolicyStatus.Running => ("En ejecución", Tone.Warn),
        PolicyStatus.Queued => ("En cola", Tone.Warn),
        PolicyStatus.NotApplicable => ("No aplica", Tone.Neutral),
        _ => ("Pendiente", Tone.Warn),
    };

    public static ReviewListItem ToPendingPr(PullRequestInfo p, string key) => new(key, ReviewKind.PullRequest,
        p.Title, $"#{p.Id}", p.CreatedByName, Initials(p.CreatedByName),
        $"{BranchName(p.SourceBranch)} → {BranchName(p.TargetBranch)}", p.CreatedOn, null, Tone.Neutral, p, null);

    public static ReviewListItem ToDeployment(PipelineApproval a, string key)
    {
        var approvers = string.Join(", ", a.Steps.Where(s => s.Status is "pending" or "").Select(s => s.ApproverName)
            .Where(n => n.Length > 0).Distinct());
        var meta = approvers.Length > 0 ? $"espera aprobación de {approvers}" : "espera aprobación";
        return new(key, ReviewKind.Deployment, a.PipelineName, "", "", "", meta, a.CreatedOn, null, Tone.Neutral, null, a);
    }

    public static ReviewListItem ToMyPr(PullRequestInfo p, string key)
    {
        var (pill, tone) = MyPrPill(p);
        return new(key, ReviewKind.MyPullRequest, p.Title, $"#{p.Id}", p.CreatedByName, Initials(p.CreatedByName),
            $"{BranchName(p.SourceBranch)} → {BranchName(p.TargetBranch)}", p.CreatedOn, pill, tone, p, null);
    }

    // Estado de mi PR según los votos de los revisores.
    public static (string Text, Tone Tone) MyPrPill(PullRequestInfo p)
    {
        var votes = p.Reviewers.Select(r => r.Vote).ToList();
        if (votes.Contains(Vote.Rejected)) return ("Rechazado", Tone.Bad);
        if (votes.Contains(Vote.WaitingForAuthor)) return ("Esperando al autor", Tone.Warn);
        if (votes.Count > 0 && votes.All(v => v is Vote.Approved or Vote.ApprovedWithSuggestions))
            return ("Aprobado", Tone.Good);
        return ("Esperando revisores", Tone.Neutral);
    }

    public static ReviewerRow ToReviewerRow(PullRequestReviewer r, string myId)
    {
        var (text, tone) = VoteBadge(r.Vote);
        return new(r.DisplayName, Initials(r.DisplayName), text, tone,
            string.Equals(r.Id, myId, StringComparison.OrdinalIgnoreCase), r.IsRequired);
    }

    public static PolicyRow ToPolicyRow(PolicyEvaluation e)
    {
        var (text, tone) = PolicyBadge(e.Status);
        return new(e.Name, text, tone);
    }

    // Motivo por el que "Completar" no está disponible (null = disponible).
    public static string? CompleteBlockReason(PullRequestInfo pr, PolicySummary summary)
    {
        if (pr.IsDraft) return "Es un borrador";
        if (string.Equals(pr.MergeStatus, "conflicts", StringComparison.OrdinalIgnoreCase)) return "Hay conflictos de fusión";
        return summary.CanComplete ? null : summary.Label;
    }
}
