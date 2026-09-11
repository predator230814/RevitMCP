using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal static class GetElementsRequestValidator
{
    public const int MinElementRefs = 1;
    public const int MaxElementRefs = 10;
    public const int MaxFields = 5;
    public const int MinParameterNames = 1;
    public const int MaxParameterNames = 10;
    public const int MaxParameterNameLength = 256;

    public static void Validate(GetElementsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DocumentId is null)
        {
            throw InvalidInspection();
        }

        if (request.ElementRefs is null
            || request.ElementRefs.Count is < MinElementRefs or > MaxElementRefs)
        {
            throw InvalidInspection();
        }

        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var elementRef in request.ElementRefs)
        {
            if (elementRef is null || !refs.Add(elementRef))
            {
                throw InvalidInspection();
            }
        }

        if (request.Projection is null)
        {
            throw InvalidInspection();
        }

        ValidateProjection(request.Projection);
    }

    public static void ValidateProjection(GetElementsProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var hasFields = projection.Fields is not null;
        var hasParameterNames = projection.ParameterNames is not null;
        if (!hasFields && !hasParameterNames)
        {
            throw InvalidInspection();
        }

        if (hasFields)
        {
            ValidateFields(projection.Fields!);
        }

        if (hasParameterNames)
        {
            ValidateParameterNames(projection.ParameterNames!);
        }
    }

    private static void ValidateFields(IReadOnlyList<GetElementField> fields)
    {
        if (fields.Count is < 1 or > MaxFields)
        {
            throw InvalidInspection();
        }

        var seen = new HashSet<GetElementField>();
        foreach (var field in fields)
        {
            if (!Enum.IsDefined(field) || !seen.Add(field))
            {
                throw InvalidInspection();
            }
        }
    }

    private static void ValidateParameterNames(IReadOnlyList<string> parameterNames)
    {
        if (parameterNames.Count is < MinParameterNames or > MaxParameterNames)
        {
            throw InvalidInspection();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in parameterNames)
        {
            if (name is null
                || name.Length is < 1 or > MaxParameterNameLength
                || !seen.Add(name))
            {
                throw InvalidInspection();
            }
        }
    }

    private static BridgeException InvalidInspection()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidInspection,
            "The element inspection request is invalid.");
    }
}
