using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Inspection;

internal readonly record struct ValidatedGetRoomLayoutsRequest(string DocumentId, IReadOnlyList<string> ElementRefs);

internal static class GetRoomLayoutsRequestValidator
{
    public static ValidatedGetRoomLayoutsRequest Validate(GetRoomLayoutsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId is null
            || request.ElementRefs is null
            || request.ElementRefs.Count is < RoomLayoutLimits.MinElementRefs or > RoomLayoutLimits.MaxElementRefs)
        {
            throw Invalid();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var refs = new List<string>(request.ElementRefs.Count);
        foreach (var elementRef in request.ElementRefs)
        {
            if (elementRef is null
                || elementRef.Length is < 1 or > RoomLayoutLimits.MaxRefLength
                || !seen.Add(elementRef))
            {
                throw Invalid();
            }

            refs.Add(elementRef);
        }

        return new ValidatedGetRoomLayoutsRequest(request.DocumentId, refs);
    }

    private static BridgeException Invalid()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidRoomLayout,
            "The room layout request is invalid.");
    }
}
