using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Query;

internal static class QueryElementsRequestValidator
{
    public const int MinLimit = 1;
    public const int MaxLimit = 100;
    public const int MaxFilterValues = 20;
    public const int MaxFilterLength = 256;

    public static void Validate(QueryElementsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Scope is not (QueryScope.Document or QueryScope.ActiveView))
        {
            throw InvalidQuery();
        }

        if (request.Limit is < MinLimit or > MaxLimit)
        {
            throw InvalidQuery();
        }

        if (request.Filters is null)
        {
            throw InvalidQuery();
        }

        ValidateFilters(request.Filters);
    }

    public static void ValidateFilters(QueryElementFilters filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var hasFilter = false;
        hasFilter |= ValidateArray(filters.CategoryNames);
        hasFilter |= ValidateArray(filters.FamilyNames);
        hasFilter |= ValidateArray(filters.TypeNames);
        hasFilter |= ValidateArray(filters.LevelNames);
        hasFilter |= ValidateText(filters.TextContains);

        if (!hasFilter)
        {
            throw InvalidQuery();
        }
    }

    private static bool ValidateArray(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return false;
        }

        if (values.Count is < 1 or > MaxFilterValues)
        {
            throw InvalidQuery();
        }

        foreach (var value in values)
        {
            if (value is null || value.Length is < 1 or > MaxFilterLength)
            {
                throw InvalidQuery();
            }
        }

        return true;
    }

    private static bool ValidateText(string? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value.Length is < 1 or > MaxFilterLength)
        {
            throw InvalidQuery();
        }

        return true;
    }

    private static BridgeException InvalidQuery()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidQuery,
            "The query request is invalid.");
    }
}
