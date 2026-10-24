namespace Rama.Core.Azure;

public enum RunStageStatus { Pending, Running, Succeeded, Failed, Canceled, Skipped, WaitingForApproval }

public sealed record RunStage(string Name, string State, string? Result, RunStageStatus Status);

// Etapas de una ejecución a partir del timeline de build (forma verificada 2026-10-23).
public static class RunStages
{
    internal static IReadOnlyList<RunStage> FromRecords(IReadOnlyList<TimelineRecordDto> records)
    {
        var byId = records.Where(r => r.Id is not null).GroupBy(r => r.Id!).ToDictionary(g => g.Key, g => g.First());
        var waiting = records
            .Where(r => Is(r.Type, "Checkpoint.Approval") && Is(r.State, "inProgress"))
            .Select(r => StageIdOf(r, byId))
            .Where(id => id is not null)
            .ToHashSet();

        return records.Where(r => Is(r.Type, "Stage"))
            .Select((r, i) => (r, i))
            .OrderBy(x => x.r.Order is null).ThenBy(x => x.r.Order).ThenBy(x => x.i)
            .Select(x => new RunStage(x.r.Name ?? "", x.r.State ?? "", x.r.Result,
                MapStatus(x.r.State, x.r.Result, x.r.Id is not null && waiting.Contains(x.r.Id))))
            .ToList();
    }

    public static RunStageStatus MapStatus(string? state, string? result, bool waitingForApproval)
    {
        if (string.Equals(state, "inProgress", StringComparison.OrdinalIgnoreCase))
            return waitingForApproval ? RunStageStatus.WaitingForApproval : RunStageStatus.Running;
        if (waitingForApproval && !string.Equals(state, "completed", StringComparison.OrdinalIgnoreCase))
            return RunStageStatus.WaitingForApproval;
        if (!string.Equals(state, "completed", StringComparison.OrdinalIgnoreCase))
            return RunStageStatus.Pending;
        return result?.ToLowerInvariant() switch
        {
            "succeeded" or "succeededwithissues" => RunStageStatus.Succeeded,
            "failed" => RunStageStatus.Failed,
            "canceled" or "abandoned" => RunStageStatus.Canceled,
            "skipped" => RunStageStatus.Skipped,
            _ => RunStageStatus.Pending,
        };
    }

    private static bool Is(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Sube por parentId hasta el registro de tipo Stage (con tope para evitar ciclos).
    private static string? StageIdOf(TimelineRecordDto r, Dictionary<string, TimelineRecordDto> byId)
    {
        var cur = r;
        for (var i = 0; i < 16 && cur is not null; i++)
        {
            if (Is(cur.Type, "Stage")) return cur.Id;
            cur = cur.ParentId is not null && byId.TryGetValue(cur.ParentId, out var p) ? p : null;
        }
        return null;
    }
}
