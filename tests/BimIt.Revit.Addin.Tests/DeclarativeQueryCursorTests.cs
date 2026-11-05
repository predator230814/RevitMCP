using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

/// <summary>
/// The source-tagged cursor primitive shared by the ADR-0023 declarative query sources,
/// exercised directly and through CAP-0014 <c>revit_query_family_types</c>.
/// </summary>
public sealed class DeclarativeQueryCursorTests
{
    private const string OtherSource = "test_other_source";

    private static readonly byte[] Key = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");

    private readonly DeclarativeQueryCursorCodec _cursors = new(Key);

    private static readonly FamilyTypeCandidate[] Rows =
    [
        Row("a", "A"),
        Row("b", "B"),
        Row("c", "C")
    ];

    [Fact]
    public void A_family_type_cursor_round_trips_with_its_source_and_bindings()
    {
        var first = Execute(Request(pageSize: 1), revision: 3);
        Assert.False(first.Complete);

        Assert.True(_cursors.TryOpen(first.NextCursor, DeclarativeQuerySources.FamilyTypes, out var payload));
        Assert.Equal(DeclarativeQuerySources.FamilyTypes, payload.Source);
        Assert.Equal("instance", payload.InstanceId);
        Assert.Equal("doc", payload.DocumentId);
        Assert.Equal(FamilyTypeFingerprint(Request()), payload.Fingerprint);
        Assert.Equal(3, payload.Revision);
        Assert.Equal(1, payload.Offset);

        var second = Execute(Request(pageSize: 1, cursor: first.NextCursor), revision: 3);
        Assert.Equal("b", second.Types[0].TypeRef);
    }

    [Fact]
    public void A_forged_mac_or_body_is_rejected()
    {
        var cursor = Execute(Request(pageSize: 1)).NextCursor!;
        var split = cursor.IndexOf('.');
        Assert.True(DeclarativeQueryCursorCodec.Base64Url.TryDecode(cursor[..split], out var body));
        Assert.True(DeclarativeQueryCursorCodec.Base64Url.TryDecode(cursor[(split + 1)..], out var mac));

        var badMac = mac.ToArray();
        badMac[0] ^= 0x01;
        var forgedMac = Encode(body, badMac);
        var badBody = body.ToArray();
        badBody[^1] ^= 0x01;
        var forgedBody = Encode(badBody, mac);

        Assert.False(_cursors.TryOpen(forgedMac, DeclarativeQuerySources.FamilyTypes, out _));
        Assert.False(_cursors.TryOpen(forgedBody, DeclarativeQuerySources.FamilyTypes, out _));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: forgedMac)).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: forgedBody)).ErrorCode);

        // A non-canonical spelling of the same bytes (changed unused trailing bits) is refused too.
        var macText = cursor[(split + 1)..];
        var last = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".IndexOf(macText[^1]);
        var sibling = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"[last ^ 0x01];
        var nonCanonical = cursor[..^1] + sibling;
        Assert.True(DeclarativeQueryCursorCodec.Base64Url.TryDecode(macText, out _));
        Assert.False(_cursors.TryOpen(nonCanonical, DeclarativeQuerySources.FamilyTypes, out _));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: nonCanonical)).ErrorCode);

        var otherProcess = new DeclarativeQueryCursorCodec(Encoding.ASCII.GetBytes("fedcba9876543210fedcba9876543210"));
        Assert.False(otherProcess.TryOpen(cursor, DeclarativeQuerySources.FamilyTypes, out _));
    }

    [Fact]
    public void The_wrong_instance_is_rejected()
    {
        var cursor = Mint(DeclarativeQuerySources.FamilyTypes, instanceId: "other-instance");
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: cursor)).ErrorCode);
    }

    [Fact]
    public void The_wrong_document_is_rejected()
    {
        var cursor = Mint(DeclarativeQuerySources.FamilyTypes, documentId: "other-doc");
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: cursor)).ErrorCode);
    }

    [Fact]
    public void A_changed_query_fingerprint_is_rejected()
    {
        var cursor = Execute(Request(pageSize: 1)).NextCursor;
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: cursor, typeName: "A")).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: cursor, usage: "unused")).ErrorCode);

        var otherFingerprint = Mint(DeclarativeQuerySources.FamilyTypes, fingerprint: new byte[32]);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: otherFingerprint)).ErrorCode);
    }

    [Fact]
    public void A_revision_change_still_returns_stale_cursor()
    {
        var cursor = Execute(Request(pageSize: 1), revision: 3).NextCursor;
        Assert.Equal(CapabilityErrorCodes.StaleCursor, Error(Request(pageSize: 1, cursor: cursor), revision: 4).ErrorCode);
        Assert.Equal("a", Execute(Request(pageSize: 1), revision: 4).Types[0].TypeRef);
    }

    [Fact]
    public void Page_size_stays_outside_the_query_fingerprint()
    {
        var unspecified = FamilyTypeFingerprint(Request(pageSize: null));
        Assert.Equal(unspecified, FamilyTypeFingerprint(Request(pageSize: 1)));
        Assert.Equal(unspecified, FamilyTypeFingerprint(Request(pageSize: 200)));
        Assert.NotEqual(unspecified, FamilyTypeFingerprint(Request(usage: "placed")));
        Assert.Equal(32, unspecified.Length);
    }

    [Fact]
    public void Page_size_can_change_between_pages()
    {
        var first = Execute(Request(pageSize: 1));
        var rest = Execute(Request(pageSize: 2, cursor: first.NextCursor));
        Assert.Equal(["b", "c"], rest.Types.Select(row => row.TypeRef).ToArray());
        Assert.True(rest.Complete);
        Assert.Null(rest.NextCursor);

        var wide = Execute(Request(pageSize: 2));
        var last = Execute(Request(pageSize: 1, cursor: wide.NextCursor));
        Assert.Equal(["c"], last.Types.Select(row => row.TypeRef).ToArray());
        Assert.True(last.Complete);
    }

    [Fact]
    public void A_cursor_of_another_source_is_invalid_even_at_a_different_revision()
    {
        var foreign = Mint(OtherSource);
        Assert.False(_cursors.TryOpen(foreign, DeclarativeQuerySources.FamilyTypes, out _));
        Assert.True(_cursors.TryOpen(foreign, OtherSource, out var opened));
        Assert.Equal(OtherSource, opened.Source);

        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: foreign)).ErrorCode);

        // The source check precedes the revision check, so it is never reported as stale.
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: foreign), revision: 9).ErrorCode);
    }

    [Fact]
    public void The_same_fingerprint_bytes_under_two_sources_never_give_interchangeable_cursors()
    {
        var fingerprint = FamilyTypeFingerprint(Request());
        var family = Mint(DeclarativeQuerySources.FamilyTypes, fingerprint: fingerprint);
        var other = Mint(OtherSource, fingerprint: fingerprint);
        Assert.NotEqual(family, other);

        Assert.False(_cursors.TryOpen(family, OtherSource, out _));
        Assert.False(_cursors.TryOpen(other, DeclarativeQuerySources.FamilyTypes, out _));

        // The MAC covers the source: a family-type MAC cannot be grafted onto another source's body.
        var familyMac = family[(family.IndexOf('.') + 1)..];
        var otherBody = other[..other.IndexOf('.')];
        Assert.False(_cursors.TryOpen(otherBody + "." + familyMac, OtherSource, out _));
        Assert.False(_cursors.TryOpen(otherBody + "." + familyMac, DeclarativeQuerySources.FamilyTypes, out _));

        // Identical field values give different fingerprints under different sources.
        var left = new DeclarativeQueryFingerprintBuilder(DeclarativeQuerySources.FamilyTypes).Value("all").Set(["OST_Doors"]).Compute();
        var right = new DeclarativeQueryFingerprintBuilder(OtherSource).Value("all").Set(["OST_Doors"]).Compute();
        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Malformed_and_wrong_version_cursors_are_rejected()
    {
        foreach (var token in new[] { null, "", "a", "a.", ".a", "a.b.c", "!!!.???", "abcde.abc", new string('a', 2049) })
        {
            Assert.False(_cursors.TryOpen(token, DeclarativeQuerySources.FamilyTypes, out _));
        }

        var fingerprint = FamilyTypeFingerprint(Request());

        // A correctly signed version-1 body (the CAP-0014-only format, no source) is refused.
        Assert.False(_cursors.TryOpen(Sign(Body(1, null, fingerprint, offset: 1)), DeclarativeQuerySources.FamilyTypes, out _));

        // Correctly signed version-2 bodies that are otherwise malformed are refused.
        Assert.True(_cursors.TryOpen(Sign(Body(2, DeclarativeQuerySources.FamilyTypes, fingerprint, offset: 1)), DeclarativeQuerySources.FamilyTypes, out _));
        Assert.False(_cursors.TryOpen(Sign(Body(2, DeclarativeQuerySources.FamilyTypes, fingerprint, offset: -1)), DeclarativeQuerySources.FamilyTypes, out _));
        Assert.False(_cursors.TryOpen(Sign(Body(2, "Family Types", fingerprint, offset: 1)), "family_types", out _));
        Assert.False(_cursors.TryOpen(Sign(Body(2, "", fingerprint, offset: 1)), DeclarativeQuerySources.FamilyTypes, out _));
        Assert.False(_cursors.TryOpen(Sign([.. Body(2, DeclarativeQuerySources.FamilyTypes, fingerprint, offset: 1), 0]), DeclarativeQuerySources.FamilyTypes, out _));
        Assert.False(_cursors.TryOpen(Sign(Body(2, DeclarativeQuerySources.FamilyTypes, fingerprint, offset: 1)[..^1]), DeclarativeQuerySources.FamilyTypes, out _));

        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: Sign(Body(1, null, fingerprint, offset: 1)))).ErrorCode);
    }

    [Fact]
    public void Codec_limits_and_arguments_are_enforced()
    {
        var fingerprint = FamilyTypeFingerprint(Request());
        Assert.False(_cursors.TryMint(Payload(DeclarativeQuerySources.FamilyTypes, "instance", new string('d', 1600), fingerprint), out var tooLong));
        Assert.Equal("", tooLong);

        Assert.Throws<ArgumentException>(() => _cursors.TryMint(Payload("Not A Source", "instance", "doc", fingerprint), out _));
        Assert.Throws<ArgumentException>(() => _cursors.TryMint(Payload(DeclarativeQuerySources.FamilyTypes, "instance", "doc", new byte[31]), out _));
        Assert.Throws<ArgumentException>(() => _cursors.TryMint(Payload(DeclarativeQuerySources.FamilyTypes, "instance", "doc", fingerprint) with { Offset = -1 }, out _));
        Assert.Throws<ArgumentException>(() => _cursors.TryOpen("x", "", out _));
        Assert.Throws<ArgumentException>(() => new DeclarativeQueryFingerprintBuilder("BAD"));
        Assert.Throws<ArgumentException>(() => new DeclarativeQueryCursorCodec(new byte[31]));

        // A cursor that would exceed the length limit still fails as CAP-0014 always did.
        var longDocument = new string('d', 1600);
        var error = Assert.Throws<BridgeException>(() => FamilyTypeInventoryQuery.Execute(
            FamilyTypeQueryValidator.Validate(Request(pageSize: 1, documentId: longDocument)),
            "instance",
            longDocument,
            hasActiveDocument: true,
            isFamilyDocument: false,
            0,
            Rows,
            new Categories(),
            _cursors));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, error.ErrorCode);
        Assert.Equal("The family type query could not be continued.", error.Message);
    }

    [Fact]
    public void Absent_and_empty_fingerprint_fields_are_distinct()
    {
        static byte[] Of(Action<DeclarativeQueryFingerprintBuilder> build)
        {
            var builder = new DeclarativeQueryFingerprintBuilder(DeclarativeQuerySources.FamilyTypes);
            build(builder);
            return builder.Compute();
        }

        Assert.NotEqual(Of(b => b.Value(null)), Of(b => b.Value("-")));
        Assert.NotEqual(Of(b => b.Value(null)), Of(b => b.Value("")));
        Assert.NotEqual(Of(b => b.Set(null)), Of(b => b.Set([])));
        Assert.NotEqual(Of(b => b.Value("ab").Value("c")), Of(b => b.Value("a").Value("bc")));
        Assert.NotEqual(Of(b => b.Set(["a;1:b"])), Of(b => b.Set(["a", "b"])));
        Assert.Equal(Of(b => b.Set(["b", "a"])), Of(b => b.Set(["a", "b"])));
    }

    private string Mint(
        string source,
        string instanceId = "instance",
        string documentId = "doc",
        byte[]? fingerprint = null,
        long revision = 0,
        int offset = 1)
    {
        var payload = Payload(source, instanceId, documentId, fingerprint ?? FamilyTypeFingerprint(Request())) with
        {
            Revision = revision,
            Offset = offset
        };
        Assert.True(_cursors.TryMint(payload, out var token));
        return token;
    }

    private static DeclarativeQueryCursorPayload Payload(string source, string instanceId, string documentId, byte[] fingerprint)
    {
        return new DeclarativeQueryCursorPayload(source, instanceId, documentId, fingerprint, 0, 1);
    }

    private static byte[] Body(byte version, string? source, byte[] fingerprint, int offset)
    {
        var parts = new List<byte> { version };
        if (source is not null)
        {
            AddText(parts, source);
        }

        AddText(parts, "instance");
        AddText(parts, "doc");
        parts.AddRange(fingerprint);
        var tail = new byte[12];
        BinaryPrimitives.WriteInt64BigEndian(tail, 0);
        BinaryPrimitives.WriteInt32BigEndian(tail.AsSpan(8), offset);
        parts.AddRange(tail);
        return parts.ToArray();
    }

    private static void AddText(List<byte> parts, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
        parts.AddRange(length);
        parts.AddRange(bytes);
    }

    private static string Sign(byte[] body)
    {
        return Encode(body, HMACSHA256.HashData(Key, body));
    }

    private static string Encode(byte[] body, byte[] mac)
    {
        return DeclarativeQueryCursorCodec.Base64Url.Encode(body) + "." + DeclarativeQueryCursorCodec.Base64Url.Encode(mac);
    }

    private static byte[] FamilyTypeFingerprint(QueryFamilyTypesRequest request)
    {
        return FamilyTypeQueryFingerprint.Compute(FamilyTypeQueryValidator.Validate(request));
    }

    private QueryFamilyTypesResult Execute(QueryFamilyTypesRequest request, long revision = 0)
    {
        return FamilyTypeInventoryQuery.Execute(
            FamilyTypeQueryValidator.Validate(request),
            "instance",
            "doc",
            hasActiveDocument: true,
            isFamilyDocument: false,
            revision,
            Rows,
            new Categories(),
            _cursors);
    }

    private BridgeException Error(QueryFamilyTypesRequest request, long revision = 0)
    {
        return Assert.Throws<BridgeException>(() => Execute(request, revision));
    }

    private static QueryFamilyTypesRequest Request(
        int? pageSize = null,
        string? cursor = null,
        string? typeName = null,
        string? usage = null,
        string documentId = "doc")
    {
        return new QueryFamilyTypesRequest
        {
            DocumentId = documentId,
            TypeNameContains = typeName,
            Usage = usage,
            PageSize = pageSize,
            Cursor = cursor
        };
    }

    private static FamilyTypeCandidate Row(string typeRef, string typeName)
    {
        return new FamilyTypeCandidate(typeRef, "f", "OST_Doors", "Doors", "Alpha", typeName, "one_level_based", false, 0);
    }

    private sealed class Categories : IBuiltInCategoryCatalog
    {
        public bool IsDefinedMember(string token) => token == "OST_Doors";
    }
}
