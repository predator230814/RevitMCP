using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal readonly record struct ValidatedGetWarningsRequest(
    string DocumentId,
    WarningSeverity? Severity,
    string? FailureKey,
    IReadOnlyList<string>? ElementRefs,
    int MaxWarnings,
    int MaxElementsPerWarning);

internal static class GetWarningsRequestValidator
{
    public const int MinElementRefs = 1;
    public const int MaxElementRefs = 10;
    public const int DefaultMaxWarnings = 25;
    public const int MinMaxWarnings = 1;
    public const int MaxMaxWarnings = 100;
    public const int DefaultMaxElementsPerWarning = 10;
    public const int MinMaxElementsPerWarning = 1;
    public const int MaxMaxElementsPerWarning = 20;

    public static ValidatedGetWarningsRequest Validate(GetWarningsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DocumentId is null)
        {
            throw Invalid();
        }

        if (request.Severity is WarningSeverity severity && !Enum.IsDefined(severity))
        {
            throw Invalid();
        }

        if (request.ElementRefs is not null)
        {
            if (request.ElementRefs.Count is < MinElementRefs or > MaxElementRefs)
            {
                throw Invalid();
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var elementRef in request.ElementRefs)
            {
                if (elementRef is null || !seen.Add(elementRef))
                {
                    throw Invalid();
                }
            }
        }

        var maxWarnings = request.MaxWarnings ?? DefaultMaxWarnings;
        var maxElements = request.MaxElementsPerWarning ?? DefaultMaxElementsPerWarning;
        if (maxWarnings is < MinMaxWarnings or > MaxMaxWarnings
            || maxElements is < MinMaxElementsPerWarning or > MaxMaxElementsPerWarning)
        {
            throw Invalid();
        }

        return new ValidatedGetWarningsRequest(
            request.DocumentId,
            request.Severity,
            request.FailureKey,
            request.ElementRefs,
            maxWarnings,
            maxElements);
    }

    private static BridgeException Invalid()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidWarnings,
            "The warnings request is invalid.");
    }
}
