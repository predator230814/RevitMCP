using System.Collections.Concurrent;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

internal static class FamilyTypeQueryLimits
{
    public const int DefaultPageSize = 100;

    public const int MaxPageSize = 200;

    public const int MaxCategoryTokens = DeclarativeQueryFilterRules.MaxCategoryTokens;

    public const int MaxTokenLength = DeclarativeQueryFilterRules.MaxCategoryTokenLength;

    public const int MaxNameLength = DeclarativeQueryFilterRules.MaxNameLength;

    public const int MaxCursorLength = DeclarativeQueryCursorCodec.MaxCursorLength;

    public const string UsageAll = "all";

    public const string UsagePlaced = "placed";

    public const string UsageUnused = "unused";
}

internal readonly record struct ValidatedFamilyTypeQuery(
    string DocumentId,
    IReadOnlyList<string>? CategoryTokens,
    string? FamilyNameContains,
    string? TypeNameContains,
    string Usage,
    int PageSize,
    string? Cursor);

internal sealed record FamilyTypeCandidate(
    string TypeRef,
    string FamilyRef,
    string CategoryToken,
    string CategoryName,
    string FamilyName,
    string TypeName,
    string FamilyPlacementType,
    bool FamilyIsInPlace,
    int PlacedInstanceCount);

internal interface IBuiltInCategoryCatalog
{
    bool IsDefinedMember(string token);
}

internal static class FamilyPlacementTokens
{
    public const string Unknown = DeclarativeQueryFilterRules.UnknownCategoryToken;

    public static string FromRevitName(string? memberName)
    {
        return memberName switch
        {
            "OneLevelBased" => "one_level_based",
            "OneLevelBasedHosted" => "one_level_based_hosted",
            "TwoLevelsBased" => "two_levels_based",
            "ViewBased" => "view_based",
            "WorkPlaneBased" => "work_plane_based",
            "CurveBased" => "curve_based",
            "CurveBasedDetail" => "curve_based_detail",
            "CurveDrivenStructural" => "curve_driven_structural",
            "Adaptive" => "adaptive",
            "Invalid" => "invalid",
            _ => Unknown
        };
    }

    public static string Read(Func<string?> readMemberName)
    {
        try
        {
            return FromRevitName(readMemberName());
        }
        catch (Exception)
        {
            return Unknown;
        }
    }
}

internal static class FamilyTypeQueryValidator
{
    public static ValidatedFamilyTypeQuery Validate(QueryFamilyTypesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId is null)
        {
            throw InvalidQuery();
        }

        var usage = request.Usage ?? FamilyTypeQueryLimits.UsageAll;
        if (usage is not (FamilyTypeQueryLimits.UsageAll or FamilyTypeQueryLimits.UsagePlaced or FamilyTypeQueryLimits.UsageUnused))
        {
            throw InvalidQuery();
        }

        var pageSize = request.PageSize ?? FamilyTypeQueryLimits.DefaultPageSize;
        if (pageSize is < 1 or > FamilyTypeQueryLimits.MaxPageSize)
        {
            throw InvalidQuery();
        }

        if (request.Cursor is not null
            && (request.Cursor.Length is < 1 or > FamilyTypeQueryLimits.MaxCursorLength))
        {
            throw InvalidCursor();
        }

        IReadOnlyList<string>? tokens = null;
        if (request.CategoryTokens is not null)
        {
            tokens = DeclarativeQueryFilterRules.CategoryTokens(request.CategoryTokens) ?? throw InvalidQuery();
        }

        return new ValidatedFamilyTypeQuery(
            request.DocumentId,
            tokens,
            NameFilter(request.FamilyNameContains),
            NameFilter(request.TypeNameContains),
            usage,
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

    private static string? NameFilter(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (!DeclarativeQueryFilterRules.IsNameFilter(value))
        {
            throw InvalidQuery();
        }

        return value;
    }

    private static BridgeException InvalidQuery()
    {
        return new BridgeException(CapabilityErrorCodes.InvalidFamilyTypeQuery, "The family type query is invalid.");
    }

    private static BridgeException InvalidCursor()
    {
        return new BridgeException(CapabilityErrorCodes.InvalidCursor, "The family type cursor is not valid.");
    }
}

internal static class FamilyTypeQueryFingerprint
{
    /// <summary>
    /// The CAP-0014 matched-set fingerprint: <c>usage</c>, both name filters, and
    /// <c>category_tokens</c> as a set, under the family-type source. Not <c>page_size</c>.
    /// </summary>
    public static byte[] Compute(ValidatedFamilyTypeQuery query)
    {
        return new DeclarativeQueryFingerprintBuilder(DeclarativeQuerySources.FamilyTypes)
            .Value(query.Usage)
            .Value(query.FamilyNameContains)
            .Value(query.TypeNameContains)
            .Set(query.CategoryTokens)
            .Compute();
    }
}

internal sealed class DocumentQueryRevisionService
{
    private readonly ConcurrentDictionary<string, long> _revisions = new(StringComparer.Ordinal);

    public long Current(string documentId)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        return _revisions.GetOrAdd(documentId, 0);
    }

    public void Advance(string documentId)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        _revisions.AddOrUpdate(documentId, 1, static (_, value) => value + 1);
    }

    public void Forget(string documentId)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        _revisions.TryRemove(documentId, out _);
    }

    /// <summary>
    /// Advances the revision of a changed document that already has a <c>document_id</c>.
    /// A document without one has never been queried, so it has no outstanding cursor,
    /// and the signal is ignored without assigning an id.
    /// </summary>
    public bool AdvanceIfKnown<TDocument>(TDocument document, TryGetDocumentId<TDocument> tryGetDocumentId)
    {
        ArgumentNullException.ThrowIfNull(tryGetDocumentId);
        if (!tryGetDocumentId(document, out var documentId))
        {
            return false;
        }

        Advance(documentId);
        return true;
    }
}

internal delegate bool TryGetDocumentId<in TDocument>(TDocument document, out string documentId);

internal static class PlacedInstanceCounts
{
    public static Dictionary<string, int> ByTypeRef(IEnumerable<string?> instanceTypeRefs, IReadOnlySet<string> symbolRefs)
    {
        ArgumentNullException.ThrowIfNull(instanceTypeRefs);
        ArgumentNullException.ThrowIfNull(symbolRefs);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var typeRef in instanceTypeRefs)
        {
            if (typeRef is null || !symbolRefs.Contains(typeRef))
            {
                continue;
            }

            counts.TryGetValue(typeRef, out var count);
            counts[typeRef] = count + 1;
        }

        return counts;
    }
}

internal readonly record struct FamilyTypeQueryPreparation(
    string InstanceId,
    string DocumentId,
    byte[] Fingerprint,
    long Revision,
    int Offset);

internal static class FamilyTypeInventoryQuery
{
    public static FamilyTypeQueryPreparation Prepare(
        ValidatedFamilyTypeQuery query,
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

        FamilyTypeQueryValidator.EnsureDefinedCategories(query.CategoryTokens, categories);
        var fingerprint = FamilyTypeQueryFingerprint.Compute(query);
        var offset = 0;
        if (query.Cursor is not null)
        {
            if (!cursors.TryOpen(query.Cursor, DeclarativeQuerySources.FamilyTypes, out var payload)
                || !string.Equals(payload.InstanceId, instanceId, StringComparison.Ordinal)
                || !string.Equals(payload.DocumentId, activeDocumentId, StringComparison.Ordinal)
                || !payload.Fingerprint.AsSpan().SequenceEqual(fingerprint))
            {
                throw new BridgeException(CapabilityErrorCodes.InvalidCursor, "The family type cursor is not valid.");
            }

            if (payload.Revision != revision)
            {
                throw new BridgeException(
                    CapabilityErrorCodes.StaleCursor,
                    "The family type query cursor is stale. Restart the query without a cursor.");
            }

            offset = payload.Offset;
        }

        return new FamilyTypeQueryPreparation(instanceId, query.DocumentId, fingerprint, revision, offset);
    }

    public static QueryFamilyTypesResult Execute(
        ValidatedFamilyTypeQuery query,
        string instanceId,
        string? activeDocumentId,
        bool hasActiveDocument,
        bool isFamilyDocument,
        long revision,
        Func<IReadOnlyList<FamilyTypeCandidate>> readInventory,
        IBuiltInCategoryCatalog categories,
        DeclarativeQueryCursorCodec cursors)
    {
        ArgumentNullException.ThrowIfNull(readInventory);
        var preparation = Prepare(query, instanceId, activeDocumentId, hasActiveDocument, isFamilyDocument, revision, categories, cursors);
        return Complete(preparation, query, readInventory(), cursors);
    }

    public static QueryFamilyTypesResult Execute(
        ValidatedFamilyTypeQuery query,
        string instanceId,
        string? activeDocumentId,
        bool hasActiveDocument,
        bool isFamilyDocument,
        long revision,
        IReadOnlyList<FamilyTypeCandidate> candidates,
        IBuiltInCategoryCatalog categories,
        DeclarativeQueryCursorCodec cursors)
    {
        var preparation = Prepare(query, instanceId, activeDocumentId, hasActiveDocument, isFamilyDocument, revision, categories, cursors);
        ArgumentNullException.ThrowIfNull(candidates);
        return Complete(preparation, query, candidates, cursors);
    }

    public static QueryFamilyTypesResult Complete(
        FamilyTypeQueryPreparation preparation,
        ValidatedFamilyTypeQuery query,
        IReadOnlyList<FamilyTypeCandidate> candidates,
        DeclarativeQueryCursorCodec cursors)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(cursors);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate.FamilyRef) || string.IsNullOrEmpty(candidate.TypeRef))
            {
                throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The family type query could not be read.");
            }
        }

        var offset = preparation.Offset;
        var fingerprint = preparation.Fingerprint;
        var revision = preparation.Revision;
        var instanceId = preparation.InstanceId;
        var matched = candidates.Where(candidate => Matches(candidate, query)).ToList();
        matched.Sort(static (left, right) =>
        {
            var category = string.CompareOrdinal(left.CategoryToken, right.CategoryToken);
            if (category != 0)
            {
                return category;
            }

            var family = string.CompareOrdinal(left.FamilyName, right.FamilyName);
            if (family != 0)
            {
                return family;
            }

            var type = string.CompareOrdinal(left.TypeName, right.TypeName);
            if (type != 0)
            {
                return type;
            }

            return string.CompareOrdinal(left.TypeRef, right.TypeRef);
        });

        if (query.Cursor is not null && (offset > matched.Count || (offset == matched.Count && matched.Count > 0)))
        {
            throw new BridgeException(CapabilityErrorCodes.InvalidCursor, "The family type cursor is not valid.");
        }

        var summary = Summarize(matched);
        var available = matched.Skip(offset).Take(query.PageSize).ToList();
        var count = Fit(available, offset, matched.Count, summary, instanceId, query.DocumentId, revision, fingerprint, cursors, out var complete, out var nextCursor);
        var page = available.Take(count).Select(ToRow).ToArray();
        return new QueryFamilyTypesResult
        {
            Context = new QueryFamilyTypesContext
            {
                InstanceId = instanceId,
                DocumentId = query.DocumentId
            },
            Summary = summary,
            ReturnedCount = page.Length,
            Complete = complete,
            NextCursor = nextCursor,
            Types = page
        };
    }

    private static int Fit(
        IReadOnlyList<FamilyTypeCandidate> available,
        int offset,
        int matchedCount,
        QueryFamilyTypesSummary summary,
        string instanceId,
        string documentId,
        long revision,
        byte[] fingerprint,
        DeclarativeQueryCursorCodec cursors,
        out bool complete,
        out string? nextCursor)
    {
        if (available.Count == 0)
        {
            complete = true;
            nextCursor = null;
            var empty = Shape(instanceId, documentId, summary, [], true, null);
            if (ReadResultBudget.Measure(empty) > ReadResultBudget.MaxCharacters)
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
                    DeclarativeQuerySources.FamilyTypes,
                    instanceId,
                    documentId,
                    fingerprint,
                    revision,
                    end);
                if (!cursors.TryMint(payload, out var minted))
                {
                    throw new BridgeException(
                        CapabilityErrorCodes.ExecutionFailed,
                        "The family type query could not be continued.");
                }

                cursor = minted;
            }

            var shaped = Shape(instanceId, documentId, summary, available.Take(count).Select(ToRow).ToArray(), isFinal, cursor);
            if (ReadResultBudget.Measure(shaped) <= ReadResultBudget.MaxCharacters)
            {
                complete = isFinal;
                nextCursor = cursor;
                return count;
            }
        }

        throw TooLarge();
    }

    private static QueryFamilyTypesResult Shape(
        string instanceId,
        string documentId,
        QueryFamilyTypesSummary summary,
        IReadOnlyList<QueryFamilyTypeRow> rows,
        bool complete,
        string? nextCursor)
    {
        return new QueryFamilyTypesResult
        {
            Context = new QueryFamilyTypesContext { InstanceId = instanceId, DocumentId = documentId },
            Summary = summary,
            ReturnedCount = rows.Count,
            Complete = complete,
            NextCursor = nextCursor,
            Types = rows
        };
    }

    private static QueryFamilyTypesSummary Summarize(IReadOnlyList<FamilyTypeCandidate> matched)
    {
        var families = new Dictionary<string, int>(StringComparer.Ordinal);
        var placedTypes = 0;
        var placedInstances = 0;
        foreach (var row in matched)
        {
            families.TryGetValue(row.FamilyRef, out var placed);
            if (row.PlacedInstanceCount > 0)
            {
                placedTypes++;
                placedInstances += row.PlacedInstanceCount;
                placed++;
            }

            families[row.FamilyRef] = placed;
        }

        var used = 0;
        foreach (var placed in families.Values)
        {
            if (placed > 0)
            {
                used++;
            }
        }

        return new QueryFamilyTypesSummary
        {
            MatchedTypeCount = matched.Count,
            DistinctFamilyCount = families.Count,
            PlacedTypeCount = placedTypes,
            PlacedInstanceCount = placedInstances,
            UsedFamilyCount = used,
            UnusedFamilyCount = families.Count - used
        };
    }

    private static bool Matches(FamilyTypeCandidate row, ValidatedFamilyTypeQuery query)
    {
        if (query.CategoryTokens is not null && !query.CategoryTokens.Contains(row.CategoryToken, StringComparer.Ordinal))
        {
            return false;
        }

        if (query.FamilyNameContains is not null
            && row.FamilyName.IndexOf(query.FamilyNameContains, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (query.TypeNameContains is not null
            && row.TypeName.IndexOf(query.TypeNameContains, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return query.Usage switch
        {
            FamilyTypeQueryLimits.UsagePlaced => row.PlacedInstanceCount > 0,
            FamilyTypeQueryLimits.UsageUnused => row.PlacedInstanceCount == 0,
            _ => true
        };
    }

    private static QueryFamilyTypeRow ToRow(FamilyTypeCandidate candidate)
    {
        return new QueryFamilyTypeRow
        {
            TypeRef = candidate.TypeRef,
            FamilyRef = candidate.FamilyRef,
            CategoryToken = candidate.CategoryToken,
            CategoryName = candidate.CategoryName,
            FamilyName = candidate.FamilyName,
            TypeName = candidate.TypeName,
            FamilyPlacementType = candidate.FamilyPlacementType,
            FamilyIsInPlace = candidate.FamilyIsInPlace,
            PlacedInstanceCount = candidate.PlacedInstanceCount,
            IsUsed = candidate.PlacedInstanceCount > 0
        };
    }

    private static BridgeException TooLarge()
    {
        return new BridgeException(CapabilityErrorCodes.ResultTooLarge, "The family type query result is too large.");
    }
}
