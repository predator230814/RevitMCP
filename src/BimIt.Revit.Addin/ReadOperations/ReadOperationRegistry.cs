using System.Text.Json;
using System.Text.RegularExpressions;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal enum ReadOperationResolution
{
    Found,
    UnknownOperation,
    UnsupportedVersion
}

/// <summary>
/// Exact-lookup outcome. <see cref="RegisteredVersions"/> is populated only for
/// <see cref="ReadOperationResolution.UnsupportedVersion"/>, in ascending order.
/// </summary>
internal sealed class ReadOperationExactLookup
{
    public required ReadOperationResolution Resolution { get; init; }

    public LookupReadOperationsResult? Result { get; init; }

    public required IReadOnlyList<int> RegisteredVersions { get; init; }
}

internal sealed class ReadOperationRegistrationException : Exception
{
    public ReadOperationRegistrationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Immutable catalogue built from an explicit operation list.
/// One invalid registration fails construction. Query, offset, and limit are already validated.
/// </summary>
internal sealed class ReadOperationRegistry
{
    private static readonly Regex OperationIdPattern = new(
        "^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly IRevitReadOperation[] _operations;

    public ReadOperationRegistry(IReadOnlyList<IRevitReadOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var copy = new IRevitReadOperation[operations.Count];
        var seen = new HashSet<(string Id, int Version)>();
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index]
                ?? throw new ReadOperationRegistrationException("A read operation registration is required.");
            var descriptor = operation.Descriptor
                ?? throw new ReadOperationRegistrationException("A read operation descriptor is required.");
            Validate(descriptor);
            if (!seen.Add((descriptor.Id, descriptor.ContractVersion)))
            {
                throw new ReadOperationRegistrationException(
                    $"Operation '{descriptor.Id}' contract version {descriptor.ContractVersion} is registered more than once.");
            }

            copy[index] = operation;
        }

        Array.Sort(copy, Compare);
        _operations = copy;
    }

    public LookupReadOperationsResult Search(string? query, int offset, int limit)
    {
        var matched = new List<ReadOperationDescriptor>();
        foreach (var operation in _operations)
        {
            if (query is null || Matches(operation.Descriptor, query))
            {
                matched.Add(WithoutSchemas(operation.Descriptor));
            }
        }

        var page = new List<ReadOperationDescriptor>();
        for (var index = offset; index < matched.Count && page.Count < limit; index++)
        {
            page.Add(matched[index]);
        }

        var returned = page.Count;
        int? nextOffset = offset + returned < matched.Count ? offset + returned : null;
        return Envelope("search", matched.Count, nextOffset, page);
    }

    public ReadOperationExactLookup LookupExact(string operationId, int? contractVersion)
    {
        var matches = new List<IRevitReadOperation>();
        foreach (var operation in _operations)
        {
            if (string.Equals(operation.Descriptor.Id, operationId, StringComparison.Ordinal))
            {
                matches.Add(operation);
            }
        }

        if (matches.Count == 0)
        {
            return new ReadOperationExactLookup
            {
                Resolution = ReadOperationResolution.UnknownOperation,
                RegisteredVersions = []
            };
        }

        if (contractVersion is int version)
        {
            var exact = new List<ReadOperationDescriptor>();
            foreach (var operation in matches)
            {
                if (operation.Descriptor.ContractVersion == version)
                {
                    exact.Add(operation.Descriptor);
                }
            }

            if (exact.Count == 0)
            {
                return new ReadOperationExactLookup
                {
                    Resolution = ReadOperationResolution.UnsupportedVersion,
                    RegisteredVersions = matches.Select(operation => operation.Descriptor.ContractVersion).ToArray()
                };
            }

            return Found(exact);
        }

        return Found(matches.Select(operation => operation.Descriptor).ToArray());
    }

    private static ReadOperationExactLookup Found(IReadOnlyList<ReadOperationDescriptor> operations)
    {
        return new ReadOperationExactLookup
        {
            Resolution = ReadOperationResolution.Found,
            Result = Envelope("exact", operations.Count, null, operations),
            RegisteredVersions = []
        };
    }

    private static LookupReadOperationsResult Envelope(
        string mode,
        int total,
        int? nextOffset,
        IReadOnlyList<ReadOperationDescriptor> operations)
    {
        return new LookupReadOperationsResult
        {
            Mode = mode,
            Total = total,
            ReturnedCount = operations.Count,
            NextOffset = nextOffset,
            Operations = operations
        };
    }

    private static bool Matches(ReadOperationDescriptor descriptor, string query)
    {
        return descriptor.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || descriptor.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || descriptor.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static ReadOperationDescriptor WithoutSchemas(ReadOperationDescriptor descriptor)
    {
        return new ReadOperationDescriptor
        {
            Id = descriptor.Id,
            ContractVersion = descriptor.ContractVersion,
            Title = descriptor.Title,
            Description = descriptor.Description,
            Context = descriptor.Context,
            Effects = descriptor.Effects,
            Pagination = descriptor.Pagination
        };
    }

    private static int Compare(IRevitReadOperation left, IRevitReadOperation right)
    {
        var id = string.CompareOrdinal(left.Descriptor.Id, right.Descriptor.Id);
        return id != 0
            ? id
            : left.Descriptor.ContractVersion.CompareTo(right.Descriptor.ContractVersion);
    }

    private static void Validate(ReadOperationDescriptor descriptor)
    {
        var id = descriptor.Id;
        if (string.IsNullOrEmpty(id)
            || id.Length > DeclarativeQuerySources.MaxLength
            || !OperationIdPattern.IsMatch(id))
        {
            throw new ReadOperationRegistrationException(
                $"Operation id '{id}' is not a lowercase dotted identifier of 1..{DeclarativeQuerySources.MaxLength} characters.");
        }

        if (descriptor.ContractVersion < 1)
        {
            throw new ReadOperationRegistrationException($"Operation '{id}' contract version must be at least 1.");
        }

        if (!string.Equals(descriptor.Effects, "read", StringComparison.Ordinal))
        {
            throw new ReadOperationRegistrationException($"Operation '{id}' effects must be read.");
        }

        if (!string.Equals(descriptor.Context, "document", StringComparison.Ordinal))
        {
            throw new ReadOperationRegistrationException($"Operation '{id}' context must be document.");
        }

        if (!string.Equals(descriptor.Pagination, "none", StringComparison.Ordinal)
            && !string.Equals(descriptor.Pagination, "cursor", StringComparison.Ordinal))
        {
            throw new ReadOperationRegistrationException($"Operation '{id}' pagination must be none or cursor.");
        }

        RequireSchema(id, "input_schema", descriptor.InputSchema);
        RequireSchema(id, "output_schema", descriptor.OutputSchema);
    }

    private static void RequireSchema(string id, string name, JsonElement? schema)
    {
        if (schema is not { } value || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ReadOperationRegistrationException($"Operation '{id}' requires {name}.");
        }

        if (!JsonSchemaSubset.TryValidateOperationSchema(value, out var error))
        {
            throw new ReadOperationRegistrationException(
                $"Operation '{id}' {name} is not a valid operation schema ({error.Path} {error.Keyword}).");
        }
    }
}
