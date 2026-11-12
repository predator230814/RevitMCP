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

/// <summary>
/// Execution resolution. The descriptor is the snapshot captured at registration.
/// <see cref="RegisteredVersions"/> is populated only for an unsupported version.
/// </summary>
internal sealed class ReadOperationExecutionResolution
{
    public required ReadOperationResolution Resolution { get; init; }

    public IExecutableReadOperation? Operation { get; init; }

    public ReadOperationDescriptor? Descriptor { get; init; }

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
/// Each descriptor is read once during construction and is the only metadata used afterward.
/// One invalid registration fails construction. Query, offset, and limit are already validated.
/// </summary>
internal sealed class ReadOperationRegistry
{
    private static readonly Regex OperationIdPattern = new(
        "^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly Registration[] _registrations;

    public ReadOperationRegistry(IReadOnlyList<IExecutableReadOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var captured = new Registration[operations.Count];
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

            captured[index] = new Registration(operation, descriptor);
        }

        Array.Sort(captured, Compare);
        _registrations = captured;
    }

    public LookupReadOperationsResult Search(string? query, int offset, int limit)
    {
        var matched = new List<ReadOperationDescriptor>();
        foreach (var registration in _registrations)
        {
            if (query is null || Matches(registration.Descriptor, query))
            {
                matched.Add(WithoutSchemas(registration.Descriptor));
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
        var matches = new List<Registration>();
        foreach (var registration in _registrations)
        {
            if (string.Equals(registration.Descriptor.Id, operationId, StringComparison.Ordinal))
            {
                matches.Add(registration);
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
            foreach (var registration in matches)
            {
                if (registration.Descriptor.ContractVersion == version)
                {
                    exact.Add(registration.Descriptor);
                }
            }

            if (exact.Count == 0)
            {
                return new ReadOperationExactLookup
                {
                    Resolution = ReadOperationResolution.UnsupportedVersion,
                    RegisteredVersions = Versions(matches)
                };
            }

            return Found(exact);
        }

        return Found(matches.Select(registration => registration.Descriptor).ToArray());
    }

    public ReadOperationExecutionResolution Resolve(string? operationId, int contractVersion)
    {
        var matches = new List<Registration>();
        foreach (var registration in _registrations)
        {
            if (string.Equals(registration.Descriptor.Id, operationId, StringComparison.Ordinal))
            {
                matches.Add(registration);
            }
        }

        if (matches.Count == 0)
        {
            return new ReadOperationExecutionResolution
            {
                Resolution = ReadOperationResolution.UnknownOperation,
                RegisteredVersions = []
            };
        }

        foreach (var registration in matches)
        {
            if (registration.Descriptor.ContractVersion == contractVersion)
            {
                return new ReadOperationExecutionResolution
                {
                    Resolution = ReadOperationResolution.Found,
                    Operation = registration.Operation,
                    Descriptor = registration.Descriptor,
                    RegisteredVersions = []
                };
            }
        }

        return new ReadOperationExecutionResolution
        {
            Resolution = ReadOperationResolution.UnsupportedVersion,
            RegisteredVersions = Versions(matches)
        };
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

    private static int[] Versions(IReadOnlyList<Registration> matches)
    {
        var versions = new int[matches.Count];
        for (var index = 0; index < matches.Count; index++)
        {
            versions[index] = matches[index].Descriptor.ContractVersion;
        }

        return versions;
    }

    private static int Compare(Registration left, Registration right)
    {
        var id = string.CompareOrdinal(left.Descriptor.Id, right.Descriptor.Id);
        return id != 0
            ? id
            : left.Descriptor.ContractVersion.CompareTo(right.Descriptor.ContractVersion);
    }

    private sealed class Registration(IExecutableReadOperation operation, ReadOperationDescriptor descriptor)
    {
        public IExecutableReadOperation Operation { get; } = operation;

        public ReadOperationDescriptor Descriptor { get; } = descriptor;
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
