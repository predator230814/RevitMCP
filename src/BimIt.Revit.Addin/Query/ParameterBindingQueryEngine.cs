using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

internal static class ParameterBindingQueryLimits
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    public const int MaxCursorLength = DeclarativeQueryCursorCodec.MaxCursorLength;

    public const string All = "all";

    public const string IdentityShared = "shared";

    public const string IdentityLocal = "local";

    public const string IdentityBuiltIn = "built_in";

    public const string BindingInstance = "instance";

    public const string BindingType = "type";
}

internal readonly record struct ValidatedParameterBindingQuery(
    string DocumentId,
    string? NameContains,
    string IdentityKind,
    string BindingKind,
    IReadOnlyList<string>? CategoryTokens,
    int PageSize,
    string? Cursor);

/// <summary>
/// One bound category as read from Revit. <paramref name="CategoryKey"/> is an internal
/// Revit-derived identity used only to deduplicate, break ties, and count distinct categories.
/// It is never returned.
/// </summary>
internal sealed record ParameterBindingCategoryCandidate(
    string CategoryToken,
    string CategoryName,
    string CategoryKey);

/// <summary>
/// One <c>Document.ParameterBindings</c> entry. The classified identity carries the internal
/// stable key used for ordering. It is never returned; only its CAP-0004 contract shape is.
/// </summary>
internal sealed record ParameterBindingCandidate(
    string ParameterRef,
    string Name,
    ClassifiedParameterIdentity Identity,
    DescribeParameterDataType DataType,
    QueryParameterBindingKind BindingKind,
    IReadOnlyList<ParameterBindingCategoryCandidate> Categories);

internal static class ParameterBindingQueryValidator
{
    public static ValidatedParameterBindingQuery Validate(QueryParameterBindingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId is null)
        {
            throw InvalidQuery();
        }

        var identityKind = request.IdentityKind ?? ParameterBindingQueryLimits.All;
        if (identityKind is not (ParameterBindingQueryLimits.All
            or ParameterBindingQueryLimits.IdentityShared
            or ParameterBindingQueryLimits.IdentityLocal
            or ParameterBindingQueryLimits.IdentityBuiltIn))
        {
            throw InvalidQuery();
        }

        var bindingKind = request.BindingKind ?? ParameterBindingQueryLimits.All;
        if (bindingKind is not (ParameterBindingQueryLimits.All
            or ParameterBindingQueryLimits.BindingInstance
            or ParameterBindingQueryLimits.BindingType))
        {
            throw InvalidQuery();
        }

        var pageSize = request.PageSize ?? ParameterBindingQueryLimits.DefaultPageSize;
        if (pageSize is < 1 or > ParameterBindingQueryLimits.MaxPageSize)
        {
            throw InvalidQuery();
        }

        if (request.Cursor is not null
            && (request.Cursor.Length is < 1 or > ParameterBindingQueryLimits.MaxCursorLength))
        {
            throw new BridgeException(CapabilityErrorCodes.InvalidCursor, "The parameter binding cursor is not valid.");
        }

        IReadOnlyList<string>? tokens = null;
        if (request.CategoryTokens is not null)
        {
            tokens = DeclarativeQueryFilterRules.CategoryTokens(request.CategoryTokens) ?? throw InvalidQuery();
        }

        if (request.NameContains is not null && !DeclarativeQueryFilterRules.IsNameFilter(request.NameContains))
        {
            throw InvalidQuery();
        }

        return new ValidatedParameterBindingQuery(
            request.DocumentId,
            request.NameContains,
            identityKind,
            bindingKind,
            tokens,
            pageSize,
            request.Cursor);
    }

    public static void EnsureDefinedCategories(IReadOnlyList<string>? tokens, IBuiltInCategoryCatalog catalog)
    {
        if (!DeclarativeQueryFilterRules.AllDefined(tokens, catalog))
        {
            throw InvalidQuery();
        }
    }

    internal static BridgeException InvalidQuery()
    {
        return new BridgeException(CapabilityErrorCodes.InvalidParameterBindingQuery, "The parameter binding query is invalid.");
    }
}

internal static class ParameterBindingQueryFingerprint
{
    /// <summary>
    /// The CAP-0015 matched-set fingerprint under the parameter-bindings source:
    /// <c>name_contains</c>, <c>identity_kind</c>, <c>binding_kind</c>, and
    /// <c>category_tokens</c> as a set. Not <c>page_size</c>.
    /// </summary>
    public static byte[] Compute(ValidatedParameterBindingQuery query)
    {
        return new DeclarativeQueryFingerprintBuilder(DeclarativeQuerySources.ParameterBindings)
            .Value(query.NameContains)
            .Value(query.IdentityKind)
            .Value(query.BindingKind)
            .Set(query.CategoryTokens)
            .Compute();
    }
}

internal readonly record struct ParameterBindingQueryPreparation(
    string InstanceId,
    string DocumentId,
    byte[] Fingerprint,
    long Revision,
    int Offset);

internal static class ParameterBindingInventoryQuery
{
    /// <summary>
    /// Document guards, category definedness, cursor integrity, and revision, in the CAP-0014
    /// order. Runs before the binding map is read, so an invalid or stale query reads nothing.
    /// </summary>
    public static ParameterBindingQueryPreparation Prepare(
        ValidatedParameterBindingQuery query,
        string instanceId,
        string? activeDocumentId,
        bool hasActiveDocument,
        bool isFamilyDocument,
        long revision,
        IBuiltInCategoryCatalog categories,
        DeclarativeQueryCursorCodec cursors)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(cursors);
        if (!hasActiveDocument)
        {
            throw new BridgeException(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.");
        }

        if (isFamilyDocument)
        {
            throw new BridgeException(CapabilityErrorCodes.UnsupportedDocumentKind, "The active document is not a project document.");
        }

        if (!string.Equals(query.DocumentId, activeDocumentId, StringComparison.Ordinal))
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentContextChanged,
                "The supplied document_id does not match the active document.");
        }

        ParameterBindingQueryValidator.EnsureDefinedCategories(query.CategoryTokens, categories);
        var fingerprint = ParameterBindingQueryFingerprint.Compute(query);
        var offset = 0;
        if (query.Cursor is not null)
        {
            if (!cursors.TryOpen(query.Cursor, DeclarativeQuerySources.ParameterBindings, out var payload)
                || !string.Equals(payload.InstanceId, instanceId, StringComparison.Ordinal)
                || !string.Equals(payload.DocumentId, activeDocumentId, StringComparison.Ordinal)
                || !payload.Fingerprint.AsSpan().SequenceEqual(fingerprint))
            {
                throw InvalidCursor();
            }

            if (payload.Revision != revision)
            {
                throw new BridgeException(
                    CapabilityErrorCodes.StaleCursor,
                    "The parameter binding query cursor is stale. Restart the query without a cursor.");
            }

            offset = payload.Offset;
        }

        return new ParameterBindingQueryPreparation(instanceId, query.DocumentId, fingerprint, revision, offset);
    }

    public static QueryParameterBindingsResult Execute(
        ValidatedParameterBindingQuery query,
        string instanceId,
        string? activeDocumentId,
        bool hasActiveDocument,
        bool isFamilyDocument,
        long revision,
        Func<IReadOnlyList<ParameterBindingCandidate>> readInventory,
        IBuiltInCategoryCatalog categories,
        DeclarativeQueryCursorCodec cursors)
    {
        ArgumentNullException.ThrowIfNull(readInventory);
        var preparation = Prepare(query, instanceId, activeDocumentId, hasActiveDocument, isFamilyDocument, revision, categories, cursors);
        return Complete(preparation, query, readInventory(), cursors);
    }

    public static QueryParameterBindingsResult Complete(
        ParameterBindingQueryPreparation preparation,
        ValidatedParameterBindingQuery query,
        IReadOnlyList<ParameterBindingCandidate> candidates,
        DeclarativeQueryCursorCodec cursors)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(cursors);
        var normalized = new List<ParameterBindingCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (candidate is null
                || string.IsNullOrEmpty(candidate.ParameterRef)
                || string.IsNullOrEmpty(candidate.Identity.StableKey)
                || candidate.Name is null
                || candidate.DataType is null
                || candidate.Categories is null)
            {
                throw Unreadable();
            }

            normalized.Add(candidate with { Categories = NormalizeCategories(candidate.Categories) });
        }

        var matched = normalized.Where(candidate => Matches(candidate, query)).ToList();
        matched.Sort(CompareRows);

        var offset = preparation.Offset;
        if (query.Cursor is not null && (offset > matched.Count || (offset == matched.Count && matched.Count > 0)))
        {
            throw InvalidCursor();
        }

        var summary = Summarize(matched);
        var available = matched.Skip(offset).Take(query.PageSize).ToList();
        var count = Fit(available, offset, matched.Count, summary, preparation, cursors, out var complete, out var nextCursor);
        return Shape(preparation, summary, available.Take(count).Select(ToRow).ToArray(), complete, nextCursor);
    }

    /// <summary>
    /// One entry per Revit category, ordered by token, then name (both ordinal), then the
    /// hidden category identity. Nothing is dropped: an <c>unknown</c> category stays.
    /// </summary>
    internal static IReadOnlyList<ParameterBindingCategoryCandidate> NormalizeCategories(
        IReadOnlyList<ParameterBindingCategoryCandidate> categories)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<ParameterBindingCategoryCandidate>(categories.Count);
        foreach (var category in categories)
        {
            if (category is null
                || category.CategoryToken is null
                || category.CategoryName is null
                || string.IsNullOrEmpty(category.CategoryKey))
            {
                throw Unreadable();
            }

            if (seen.Add(category.CategoryKey))
            {
                unique.Add(category);
            }
        }

        unique.Sort(static (left, right) =>
        {
            var token = string.CompareOrdinal(left.CategoryToken, right.CategoryToken);
            if (token != 0)
            {
                return token;
            }

            var name = string.CompareOrdinal(left.CategoryName, right.CategoryName);
            return name != 0 ? name : string.CompareOrdinal(left.CategoryKey, right.CategoryKey);
        });
        return unique;
    }

    /// <summary>
    /// CAP-0015 total order: name (<c>OrdinalIgnoreCase</c>), name (<c>Ordinal</c>), identity kind
    /// (built-in, shared, local), stable identity key (<c>Ordinal</c>). Never <c>parameter_ref</c>.
    /// </summary>
    internal static int CompareRows(ParameterBindingCandidate left, ParameterBindingCandidate right)
    {
        var folded = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        if (folded != 0)
        {
            return folded;
        }

        var exact = string.CompareOrdinal(left.Name, right.Name);
        if (exact != 0)
        {
            return exact;
        }

        var kind = KindRank(left.Identity.Kind).CompareTo(KindRank(right.Identity.Kind));
        return kind != 0 ? kind : string.CompareOrdinal(left.Identity.StableKey, right.Identity.StableKey);
    }

    private static int KindRank(DescribeParameterIdentityKind kind)
    {
        return kind switch
        {
            DescribeParameterIdentityKind.BuiltIn => 0,
            DescribeParameterIdentityKind.Shared => 1,
            _ => 2
        };
    }

    private static bool Matches(ParameterBindingCandidate row, ValidatedParameterBindingQuery query)
    {
        if (query.CategoryTokens is not null
            && !row.Categories.Any(category =>
                !string.Equals(category.CategoryToken, DeclarativeQueryFilterRules.UnknownCategoryToken, StringComparison.Ordinal)
                && query.CategoryTokens.Contains(category.CategoryToken, StringComparer.Ordinal)))
        {
            return false;
        }

        if (query.NameContains is not null && !DeclarativeQueryFilterRules.NameContains(row.Name, query.NameContains))
        {
            return false;
        }

        var identityMatches = query.IdentityKind switch
        {
            ParameterBindingQueryLimits.IdentityShared => row.Identity.Kind == DescribeParameterIdentityKind.Shared,
            ParameterBindingQueryLimits.IdentityLocal => row.Identity.Kind == DescribeParameterIdentityKind.Local,
            ParameterBindingQueryLimits.IdentityBuiltIn => row.Identity.Kind == DescribeParameterIdentityKind.BuiltIn,
            _ => true
        };
        if (!identityMatches)
        {
            return false;
        }

        return query.BindingKind switch
        {
            ParameterBindingQueryLimits.BindingInstance => row.BindingKind == QueryParameterBindingKind.Instance,
            ParameterBindingQueryLimits.BindingType => row.BindingKind == QueryParameterBindingKind.Type,
            _ => true
        };
    }

    private static QueryParameterBindingsSummary Summarize(IReadOnlyList<ParameterBindingCandidate> matched)
    {
        var shared = 0;
        var local = 0;
        var builtIn = 0;
        var instance = 0;
        var type = 0;
        var categories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in matched)
        {
            switch (row.Identity.Kind)
            {
                case DescribeParameterIdentityKind.BuiltIn:
                    builtIn++;
                    break;
                case DescribeParameterIdentityKind.Shared:
                    shared++;
                    break;
                default:
                    local++;
                    break;
            }

            if (row.BindingKind == QueryParameterBindingKind.Type)
            {
                type++;
            }
            else
            {
                instance++;
            }

            foreach (var category in row.Categories)
            {
                categories.Add(category.CategoryKey);
            }
        }

        return new QueryParameterBindingsSummary
        {
            MatchedDefinitionCount = matched.Count,
            SharedCount = shared,
            LocalCount = local,
            BuiltInCount = builtIn,
            InstanceBoundCount = instance,
            TypeBoundCount = type,
            DistinctCategoryCount = categories.Count
        };
    }

    private static int Fit(
        IReadOnlyList<ParameterBindingCandidate> available,
        int offset,
        int matchedCount,
        QueryParameterBindingsSummary summary,
        ParameterBindingQueryPreparation preparation,
        DeclarativeQueryCursorCodec cursors,
        out bool complete,
        out string? nextCursor)
    {
        if (available.Count == 0)
        {
            complete = true;
            nextCursor = null;
            if (ReadResultBudget.Measure(Shape(preparation, summary, [], true, null)) > ReadResultBudget.MaxCharacters)
            {
                throw TooLarge();
            }

            return 0;
        }

        for (var count = available.Count; count >= 1; count--)
        {
            var end = offset + count;
            var isFinal = end >= matchedCount;
            string? cursor = null;
            if (!isFinal)
            {
                var payload = new DeclarativeQueryCursorPayload(
                    DeclarativeQuerySources.ParameterBindings,
                    preparation.InstanceId,
                    preparation.DocumentId,
                    preparation.Fingerprint,
                    preparation.Revision,
                    end);
                if (!cursors.TryMint(payload, out var minted))
                {
                    throw new BridgeException(
                        CapabilityErrorCodes.ExecutionFailed,
                        "The parameter binding query could not be continued.");
                }

                cursor = minted;
            }

            var shaped = Shape(preparation, summary, available.Take(count).Select(ToRow).ToArray(), isFinal, cursor);
            if (ReadResultBudget.Measure(shaped) <= ReadResultBudget.MaxCharacters)
            {
                complete = isFinal;
                nextCursor = cursor;
                return count;
            }
        }

        throw TooLarge();
    }

    private static QueryParameterBindingsResult Shape(
        ParameterBindingQueryPreparation preparation,
        QueryParameterBindingsSummary summary,
        IReadOnlyList<QueryParameterBindingRow> rows,
        bool complete,
        string? nextCursor)
    {
        return new QueryParameterBindingsResult
        {
            Context = new QueryParameterBindingsContext
            {
                InstanceId = preparation.InstanceId,
                DocumentId = preparation.DocumentId
            },
            Summary = summary,
            ReturnedCount = rows.Count,
            Complete = complete,
            NextCursor = nextCursor,
            Bindings = rows
        };
    }

    private static QueryParameterBindingRow ToRow(ParameterBindingCandidate candidate)
    {
        return new QueryParameterBindingRow
        {
            ParameterRef = candidate.ParameterRef,
            Name = candidate.Name,
            Identity = ParameterIdentityClassifier.ToContract(candidate.Identity),
            DataType = candidate.DataType,
            BindingKind = candidate.BindingKind,
            Categories = candidate.Categories
                .Select(static category => new QueryParameterBindingCategory
                {
                    CategoryToken = category.CategoryToken,
                    CategoryName = category.CategoryName
                })
                .ToArray()
        };
    }

    private static BridgeException InvalidCursor()
    {
        return new BridgeException(CapabilityErrorCodes.InvalidCursor, "The parameter binding cursor is not valid.");
    }

    private static BridgeException TooLarge()
    {
        return new BridgeException(CapabilityErrorCodes.ResultTooLarge, "The parameter binding query result is too large.");
    }

    internal static BridgeException Unreadable()
    {
        return new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The parameter binding query could not be read.");
    }
}
