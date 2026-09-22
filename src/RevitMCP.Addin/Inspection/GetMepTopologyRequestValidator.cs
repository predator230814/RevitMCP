using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal readonly record struct ValidatedGetMepTopologyRequest(
    string DocumentId,
    IReadOnlyList<string> SeedElementRefs,
    MepTopologyDomain? Domain,
    int MaxDepth,
    int MaxElements,
    int MaxEdges);

internal static class GetMepTopologyRequestValidator
{
    public const int MinSeeds = 1;
    public const int MaxSeeds = 10;
    public const int DefaultMaxDepth = 3;
    public const int MinMaxDepth = 1;
    public const int MaxMaxDepth = 10;
    public const int DefaultMaxElements = 100;
    public const int MinMaxElements = 10;
    public const int MaxMaxElements = 250;
    public const int DefaultMaxEdges = 200;
    public const int MinMaxEdges = 10;
    public const int MaxMaxEdges = 500;

    public static ValidatedGetMepTopologyRequest Validate(GetMepTopologyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DocumentId is null)
        {
            throw Invalid();
        }

        if (request.SeedElementRefs is null
            || request.SeedElementRefs.Count is < MinSeeds or > MaxSeeds)
        {
            throw Invalid();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in request.SeedElementRefs)
        {
            if (seed is null || !seen.Add(seed))
            {
                throw Invalid();
            }
        }

        if (request.Domain is MepTopologyDomain domain && !Enum.IsDefined(domain))
        {
            throw Invalid();
        }

        var maxDepth = request.MaxDepth ?? DefaultMaxDepth;
        var maxElements = request.MaxElements ?? DefaultMaxElements;
        var maxEdges = request.MaxEdges ?? DefaultMaxEdges;
        if (maxDepth is < MinMaxDepth or > MaxMaxDepth
            || maxElements is < MinMaxElements or > MaxMaxElements
            || maxEdges is < MinMaxEdges or > MaxMaxEdges)
        {
            throw Invalid();
        }

        return new ValidatedGetMepTopologyRequest(
            request.DocumentId,
            request.SeedElementRefs,
            request.Domain,
            maxDepth,
            maxElements,
            maxEdges);
    }

    private static BridgeException Invalid()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidMepTopology,
            "The MEP topology request is invalid.");
    }
}
