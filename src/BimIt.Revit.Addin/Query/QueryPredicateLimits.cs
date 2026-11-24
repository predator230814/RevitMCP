namespace BimIt.Revit.Addin.Query;

/// <summary>
/// Isolated CAP-0018 bounds. <see cref="MaxPredicateCandidates"/> is the v1
/// candidate budget counted after coarse filters. Live validation may tune it
/// before merge; predicate-free queries do not use it.
/// </summary>
internal static class QueryPredicateLimits
{
    public const int MaxPredicates = 8;

    public const int MaxPredicateCandidates = 10_000;

    public const int MaxParameterRefLength = 256;

    public const int MaxStringLength = 512;

    public const int MaxUnitTypeIdLength = 256;
}
