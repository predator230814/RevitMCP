using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// CAP-0016 lookup over the immutable registry. It does not enter EXEC-0001 and does not call the Revit API.
/// </summary>
internal sealed class RevitLookupReadOperationsService
{
    public const int DefaultOffset = 0;

    public const int DefaultLimit = 25;

    public const int MaxLimit = 50;

    public const int MaxQueryLength = 256;

    public const int MaxOperationLength = 64;

    private readonly ReadOperationRegistry _registry;

    public RevitLookupReadOperationsService(ReadOperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public LookupReadOperationsResult Lookup(LookupReadOperationsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = request.Operation is null ? Search(request) : Exact(request);
        if (ReadResultBudget.Measure(result) > ReadResultBudget.MaxCharacters)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ResultTooLarge,
                "The read operation lookup is too large.");
        }

        return result;
    }

    private LookupReadOperationsResult Search(LookupReadOperationsRequest request)
    {
        if (request.ContractVersion is not null)
        {
            throw Invalid("contract_version is not accepted by a search lookup.");
        }

        if (request.Query is { Length: 0 or > MaxQueryLength })
        {
            throw Invalid("query is invalid.");
        }

        var offset = request.Offset ?? DefaultOffset;
        var limit = request.Limit ?? DefaultLimit;
        if (offset < 0)
        {
            throw Invalid("offset is invalid.");
        }

        if (limit is < 1 or > MaxLimit)
        {
            throw Invalid("limit is invalid.");
        }

        return _registry.Search(request.Query, offset, limit);
    }

    private LookupReadOperationsResult Exact(LookupReadOperationsRequest request)
    {
        var illegal = new List<string>();
        if (request.Query is not null)
        {
            illegal.Add("query");
        }

        if (request.Offset is not null)
        {
            illegal.Add("offset");
        }

        if (request.Limit is not null)
        {
            illegal.Add("limit");
        }

        if (illegal.Count > 0)
        {
            var verb = illegal.Count == 1 ? "is" : "are";
            throw Invalid(string.Join(", ", illegal) + " " + verb + " not accepted by an exact lookup.");
        }

        var operation = request.Operation ?? throw Invalid("operation is invalid.");
        if (operation.Length is < 1 or > MaxOperationLength)
        {
            throw Invalid("operation is invalid.");
        }

        if (request.ContractVersion is < 1)
        {
            throw Invalid("contract_version is invalid.");
        }

        var lookup = _registry.LookupExact(operation, request.ContractVersion);
        if (lookup.Resolution == ReadOperationResolution.UnknownOperation)
        {
            throw new BridgeException(
                CapabilityErrorCodes.UnknownReadOperation,
                "No read operation with this id is registered.");
        }

        if (lookup.Resolution == ReadOperationResolution.UnsupportedVersion)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ReadOperationVersionUnsupported,
                "The requested contract version is not registered. Registered versions: "
                + string.Join(", ", lookup.RegisteredVersions)
                + ".");
        }

        return lookup.Result ?? throw new BridgeException(
            CapabilityErrorCodes.ExecutionFailed,
            "The read operation lookup could not be read.");
    }

    private static BridgeException Invalid(string message)
    {
        return new BridgeException(CapabilityErrorCodes.InvalidReadOperationLookup, message);
    }
}
