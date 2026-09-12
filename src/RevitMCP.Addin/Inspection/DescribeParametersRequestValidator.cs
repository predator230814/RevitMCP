using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal static class DescribeParametersRequestValidator
{
    public const int MinElementRefs = 1;
    public const int MaxElementRefs = 10;
    public const int MinNameContainsLength = 1;
    public const int MaxNameContainsLength = 256;
    public const int DefaultLimit = 50;
    public const int MinLimit = 1;
    public const int MaxLimit = 100;

    public static void Validate(DescribeParametersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DocumentId is null)
        {
            throw InvalidDiscovery();
        }

        if (request.ElementRefs is null
            || request.ElementRefs.Count is < MinElementRefs or > MaxElementRefs)
        {
            throw InvalidDiscovery();
        }

        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var elementRef in request.ElementRefs)
        {
            if (elementRef is null || !refs.Add(elementRef))
            {
                throw InvalidDiscovery();
            }
        }

        if (!Enum.IsDefined(request.Source) || request.Source is not (
            DescribeParameterSource.Instance or DescribeParameterSource.Type or DescribeParameterSource.Both))
        {
            throw InvalidDiscovery();
        }

        if (request.NameContains is not null
            && request.NameContains.Length is < MinNameContainsLength or > MaxNameContainsLength)
        {
            throw InvalidDiscovery();
        }

        if (request.Limit is < MinLimit or > MaxLimit)
        {
            throw InvalidDiscovery();
        }
    }

    private static BridgeException InvalidDiscovery()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidParameterDiscovery,
            "The parameter discovery request is invalid.");
    }
}
