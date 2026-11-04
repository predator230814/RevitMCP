using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

internal static class FamilyTypeQueryLimits
{
    public const int DefaultPageSize = 100;

    public const int MaxPageSize = 200;

    public const int MaxCategoryTokens = 20;

    public const int MaxTokenLength = 80;

    public const int MaxNameLength = 256;

    public const int MaxCursorLength = 2048;

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
    public const string Unknown = "unknown";

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
            if (request.CategoryTokens.Count is < 1 or > FamilyTypeQueryLimits.MaxCategoryTokens)
            {
                throw InvalidQuery();
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var accepted = new List<string>(request.CategoryTokens.Count);
            foreach (var token in request.CategoryTokens)
            {
                if (token is null || !IsTokenShape(token) || !seen.Add(token))
                {
                    throw InvalidQuery();
                }

                accepted.Add(token);
            }

            tokens = accepted;
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
        ArgumentNullException.ThrowIfNull(catalog);
        if (tokens is null)
        {
            return;
        }

        foreach (var token in tokens)
        {
            if (!catalog.IsDefinedMember(token))
            {
                throw InvalidQuery();
            }
        }
    }

    private static string? NameFilter(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length is < 1 or > FamilyTypeQueryLimits.MaxNameLength || IsWhitespace(value))
        {
            throw InvalidQuery();
        }

        return value;
    }

    private static bool IsTokenShape(string token)
    {
        if (token.Length is < 1 or > FamilyTypeQueryLimits.MaxTokenLength)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (character is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWhitespace(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
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
    public static byte[] Compute(ValidatedFamilyTypeQuery query)
    {
        var builder = new StringBuilder();
        builder.Append(query.Usage);
        builder.Append('\n');
        Append(builder, query.FamilyNameContains);
        builder.Append('\n');
        Append(builder, query.TypeNameContains);
        builder.Append('\n');
        if (query.CategoryTokens is not null)
        {
            foreach (var token in query.CategoryTokens.OrderBy(static token => token, StringComparer.Ordinal))
            {
                builder.Append(token.Length.ToString(CultureInfo.InvariantCulture));
                builder.Append(':');
                builder.Append(token);
                builder.Append('\n');
            }
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static void Append(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append('-');
            return;
        }

        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }
}

internal readonly record struct FamilyTypeCursorPayload(
    string InstanceId,
    string DocumentId,
    byte[] Fingerprint,
    long Revision,
    int Offset);

/// <summary>
/// Stateless HMAC cursor. The signing key lives only for this process and is not serialized.
/// </summary>
internal sealed class FamilyTypeCursorCodec
{
    private readonly byte[] _key;

    public FamilyTypeCursorCodec()
        : this(RandomNumberGenerator.GetBytes(32))
    {
    }

    internal FamilyTypeCursorCodec(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
        {
            throw new ArgumentException("A 32-byte cursor key is required.", nameof(key));
        }

        _key = key.ToArray();
    }

    public string Mint(FamilyTypeCursorPayload payload)
    {
        if (payload.Offset < 0 || payload.Fingerprint is null || payload.Fingerprint.Length != 32)
        {
            throw new BridgeException(CapabilityErrorCodes.InvalidCursor, "The family type cursor is not valid.");
        }

        var body = Encode(payload);
        var mac = HMACSHA256.HashData(_key, body);
        var token = Base64Url.Encode(body) + "." + Base64Url.Encode(mac);
        if (token.Length is < 1 or > FamilyTypeQueryLimits.MaxCursorLength)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The family type query could not be continued.");
        }

        return token;
    }

    public bool TryOpen(string token, out FamilyTypeCursorPayload payload)
    {
        payload = default;
        if (string.IsNullOrEmpty(token) || token.Length > FamilyTypeQueryLimits.MaxCursorLength)
        {
            return false;
        }

        var split = token.IndexOf('.');
        if (split <= 0 || split != token.LastIndexOf('.') || split == token.Length - 1)
        {
            return false;
        }

        if (!Base64Url.TryDecode(token[..split], out var body) || !Base64Url.TryDecode(token[(split + 1)..], out var mac))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_key, body);
        if (mac.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(mac, expected))
        {
            return false;
        }

        return TryDecode(body, out payload);
    }

    private static byte[] Encode(FamilyTypeCursorPayload payload)
    {
        var instance = Encoding.UTF8.GetBytes(payload.InstanceId);
        var document = Encoding.UTF8.GetBytes(payload.DocumentId);
        if (instance.Length > ushort.MaxValue || document.Length > ushort.MaxValue)
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The family type query could not be continued.");
        }

        var buffer = new byte[1 + 2 + instance.Length + 2 + document.Length + 32 + 8 + 4];
        buffer[0] = 1;
        var offset = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)instance.Length);
        offset += 2;
        instance.CopyTo(buffer.AsSpan(offset));
        offset += instance.Length;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)document.Length);
        offset += 2;
        document.CopyTo(buffer.AsSpan(offset));
        offset += document.Length;
        payload.Fingerprint.CopyTo(buffer.AsSpan(offset));
        offset += 32;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset), payload.Revision);
        offset += 8;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset), payload.Offset);
        return buffer;
    }

    private static bool TryDecode(byte[] body, out FamilyTypeCursorPayload payload)
    {
        payload = default;
        if (body.Length < 1 + 2 + 2 + 32 + 8 + 4 || body[0] != 1)
        {
            return false;
        }

        var offset = 1;
        if (!TryReadText(body, ref offset, out var instanceId) || !TryReadText(body, ref offset, out var documentId))
        {
            return false;
        }

        if (body.Length - offset < 32 + 8 + 4)
        {
            return false;
        }

        var fingerprint = body.AsSpan(offset, 32).ToArray();
        offset += 32;
        var revision = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(body.AsSpan(offset));
        offset += 8;
        var rowOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(offset));
        offset += 4;
        if (offset != body.Length || rowOffset < 0)
        {
            return false;
        }

        payload = new FamilyTypeCursorPayload(instanceId, documentId, fingerprint, revision, rowOffset);
        return true;
    }

    private static bool TryReadText(byte[] body, ref int offset, out string text)
    {
        text = "";
        if (body.Length - offset < 2)
        {
            return false;
        }

        var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(offset));
        offset += 2;
        if (body.Length - offset < length)
        {
            return false;
        }

        text = Encoding.UTF8.GetString(body, offset, length);
        offset += length;
        return true;
    }

    private static class Base64Url
    {
        public static string Encode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static bool TryDecode(string text, out byte[] bytes)
        {
            bytes = [];
            if (text.Length == 0)
            {
                return false;
            }

            var padded = text.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
                case 0:
                    break;
                default:
                    return false;
            }

            try
            {
                bytes = Convert.FromBase64String(padded);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
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
}

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

internal static class FamilyTypeInventoryQuery
{
    public static QueryFamilyTypesResult Execute(
        ValidatedFamilyTypeQuery query,
        string instanceId,
        string? activeDocumentId,
        bool hasActiveDocument,
        bool isFamilyDocument,
        long revision,
        IReadOnlyList<FamilyTypeCandidate> candidates,
        IBuiltInCategoryCatalog categories,
        FamilyTypeCursorCodec cursors)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentNullException.ThrowIfNull(candidates);
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
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate.FamilyRef) || string.IsNullOrEmpty(candidate.TypeRef))
            {
                throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The family type query could not be read.");
            }
        }

        var fingerprint = FamilyTypeQueryFingerprint.Compute(query);
        var offset = 0;
        if (query.Cursor is not null)
        {
            if (!cursors.TryOpen(query.Cursor, out var payload)
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
        FamilyTypeCursorCodec cursors,
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
                cursor = cursors.Mint(new FamilyTypeCursorPayload(instanceId, documentId, fingerprint, revision, end));
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
