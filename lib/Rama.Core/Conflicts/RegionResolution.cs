namespace Rama.Core.Conflicts;

public enum ResolutionKind
{
    Unresolved,
    Ours,
    Theirs,
    BothOursFirst,
    BothTheirsFirst,
    Manual,
}

// Elección para una región en conflicto. Manual lleva el texto exacto (con fines de línea).
public sealed record RegionResolution(ResolutionKind Kind, string? ManualText = null)
{
    public static readonly RegionResolution Unresolved = new(ResolutionKind.Unresolved);
    public static readonly RegionResolution Ours = new(ResolutionKind.Ours);
    public static readonly RegionResolution Theirs = new(ResolutionKind.Theirs);
    public static readonly RegionResolution BothOursFirst = new(ResolutionKind.BothOursFirst);
    public static readonly RegionResolution BothTheirsFirst = new(ResolutionKind.BothTheirsFirst);

    public static RegionResolution Manual(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new RegionResolution(ResolutionKind.Manual, text);
    }
}

// Estado de las casillas de una región: cada lado marcado y, si ambos, cuál se marcó primero.
public sealed record RegionSelection(bool Ours, bool Theirs, bool OursFirst)
{
    public static readonly RegionSelection None = new(false, false, true);

    public RegionSelection ToggleOurs() =>
        Ours ? this with { Ours = false } : this with { Ours = true, OursFirst = !Theirs };

    public RegionSelection ToggleTheirs() =>
        Theirs ? this with { Theirs = false } : this with { Theirs = true, OursFirst = Ours };

    public RegionResolution ToResolution() => (Ours, Theirs) switch
    {
        (true, true) => OursFirst ? RegionResolution.BothOursFirst : RegionResolution.BothTheirsFirst,
        (true, false) => RegionResolution.Ours,
        (false, true) => RegionResolution.Theirs,
        _ => RegionResolution.Unresolved,
    };

    public static RegionSelection From(RegionResolution resolution) => resolution.Kind switch
    {
        ResolutionKind.Ours => new(true, false, true),
        ResolutionKind.Theirs => new(false, true, true),
        ResolutionKind.BothOursFirst => new(true, true, true),
        ResolutionKind.BothTheirsFirst => new(true, true, false),
        _ => None,
    };
}
