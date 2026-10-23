using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Inspection;

internal readonly record struct ValidatedGetMepDeviceTypesRequest(
    string DocumentId,
    IReadOnlyList<string> CategoryTokens,
    string? Search,
    int Limit);

internal static class GetMepDeviceTypesRequestValidator
{
    public const int DefaultLimit = 40;

    public const int MaxLimit = 80;

    public const int MaxSearchLength = 80;

    public static ValidatedGetMepDeviceTypesRequest Validate(GetMepDeviceTypesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DocumentId) || request.DocumentId.Length > 128)
        {
            throw Invalid();
        }

        var limit = request.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
        {
            throw Invalid();
        }

        string? search = null;
        if (request.Search is not null)
        {
            if (request.Search.Length is < 1 or > MaxSearchLength)
            {
                throw Invalid();
            }

            search = request.Search;
        }

        var tokens = request.CategoryTokens ?? MepDeviceCategorySupport.SupportedTokens;
        if (tokens.Count < 1 || tokens.Count > MepDeviceCategorySupport.SupportedTokens.Length)
        {
            throw Invalid();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            if (!MepDeviceCategorySupport.IsSupported(token) || !seen.Add(token))
            {
                throw Invalid();
            }

            accepted.Add(token);
        }

        return new ValidatedGetMepDeviceTypesRequest(request.DocumentId, accepted, search, limit);
    }

    private static BridgeException Invalid()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidMepDeviceTypeCatalog,
            "The MEP device type catalog request is invalid.");
    }
}
