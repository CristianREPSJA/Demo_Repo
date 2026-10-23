namespace Rama.Core.Jira;

public enum PlanKind { Allowed, RequiresFields, NotAllowed }

public sealed record TransitionPlan(
    PlanKind Kind,
    JiraTransition? Transition,
    IReadOnlyList<TransitionField> MissingFields,
    string? Reason);

public static class TransitionPlanner
{
    // Prefiere una transición sin campos obligatorios.
    public static JiraTransition? FindTransitionTo(IEnumerable<JiraTransition> transitions, JiraColumn target)
    {
        var candidates = transitions.Where(t => target.StatusIds.Contains(t.ToStatusId)).ToList();
        return candidates.FirstOrDefault(t => RequiredFields(t).Count == 0) ?? candidates.FirstOrDefault();
    }

    // Campos obligatorios sin valor por defecto.
    public static IReadOnlyList<TransitionField> RequiredFields(JiraTransition t) =>
        t.Fields.Where(f => f.Required && !f.HasDefault).ToList();

    public static TransitionPlan Plan(JiraIssue issue, JiraColumn target,
        IEnumerable<JiraTransition> transitions, IEnumerable<string>? providedFields = null)
    {
        var transition = FindTransitionTo(transitions, target);
        if (transition is null)
            return new TransitionPlan(PlanKind.NotAllowed, null, [],
                $"Tu flujo no permite pasar de {issue.StatusName} a {target.Name}");

        var provided = providedFields?.ToHashSet() ?? [];
        var missing = RequiredFields(transition).Where(f => !provided.Contains(f.Key)).ToList();
        return missing.Count == 0
            ? new TransitionPlan(PlanKind.Allowed, transition, [], null)
            : new TransitionPlan(PlanKind.RequiresFields, transition, missing, null);
    }
}
