using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using RevitMCP.Addin.Intents;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class EphemeralWriteIntentStoreTests
{
    [Fact]
    public void Random_source_becomes_lowercase_64_character_ref()
    {
        var first = Bytes(1);
        var second = Bytes(2);
        var draws = new Queue<byte[]>(new[] { first, second });
        var store = Store(draws);

        var created = store.TryCreate(Draft());
        var again = store.TryCreate(Draft());

        Assert.Equal(IntentStoreCreateStatus.Created, created.Status);
        Assert.Equal(64, created.IntentRef!.Length);
        Assert.Equal(IntentCanonicalEncoder.ToLowerHex(first), created.IntentRef);
        Assert.Equal(created.IntentRef, created.IntentRef.ToLowerInvariant());
        Assert.NotEqual(created.IntentRef, again.IntentRef);
    }

    [Fact]
    public void Colliding_draw_is_replaced_and_eight_collisions_reject_without_overwrite()
    {
        var existing = Bytes(4);
        var fresh = Bytes(5);
        var draws = new Queue<byte[]>(new[] { existing, existing, fresh });
        var store = Store(draws);
        var first = store.TryCreate(Draft());

        var redrawn = store.TryCreate(Draft());

        Assert.Equal(IntentCanonicalEncoder.ToLowerHex(fresh), redrawn.IntentRef);
        Assert.True(store.TryGet(first.IntentRef!, out var original));
        Assert.Equal("Wall 1", original!.Items[0].ElementName);

        var stuck = new byte[32];
        stuck[0] = 0xAB;
        var collisions = new Queue<byte[]>();
        collisions.Enqueue(stuck);
        for (var draw = 0; draw < EphemeralWriteIntentStore.MaxRandomDraws; draw++)
        {
            collisions.Enqueue(stuck);
        }

        var blocked = new EphemeralWriteIntentStore(new ManualTimeProvider(), collisions.Dequeue);
        var kept = blocked.TryCreate(Draft());
        var rejected = blocked.TryCreate(Draft(elementName: "other"));

        Assert.Equal(IntentStoreCreateStatus.Rejected, rejected.Status);
        Assert.NotEqual(IntentStoreCreateStatus.CapacityReached, rejected.Status);
        Assert.True(blocked.TryGet(kept.IntentRef!, out var still));
        Assert.Equal("Wall 1", still!.Items[0].ElementName);
        Assert.False(blocked.TryGet(IntentCanonicalEncoder.ToLowerHex(stuck).ToUpperInvariant(), out _));
    }

    [Fact]
    public void Same_semantic_intent_keeps_fingerprint_when_ref_changes()
    {
        var store = Store(new Queue<byte[]>(new[] { Bytes(1), Bytes(2) }));
        var first = store.TryCreate(Draft());
        var second = store.TryCreate(Draft());

        Assert.Equal(first.IntentFingerprint, second.IntentFingerprint);
        Assert.NotEqual(first.IntentRef, second.IntentRef);
    }

    [Fact]
    public void Semantic_field_changes_change_fingerprint()
    {
        var baseline = Fingerprint(Draft());
        var forward = TwoItems("a", "b");
        var reversed = TwoItems("b", "a");
        reversed.Items![0].ElementName = "Door 1";
        reversed.Items[0].ElementRef = "b";
        reversed.Items[1].ElementName = "Wall 1";
        reversed.Items[1].ElementRef = "a";
        Assert.NotEqual(baseline, Fingerprint(forward));
        Assert.NotEqual(Fingerprint(forward), Fingerprint(reversed));
        Assert.NotEqual(baseline, Fingerprint(Draft(elementName: "Wall 2")));
        Assert.NotEqual(baseline, Fingerprint(Draft(before: new IntentTypedValue.StringValue("old"))));
        Assert.NotEqual(baseline, Fingerprint(Draft(proposed: new IntentTypedValue.StringValue("next"))));
        Assert.NotEqual(baseline, Fingerprint(Draft(forgeTypeId: "autodesk.spec:text-1.0.0")));
        Assert.NotEqual(baseline, Fingerprint(Draft(parameterTypeId: "autodesk.parameter:local")));
    }

    [Fact]
    public void Negative_zero_matches_positive_zero_canonical_bytes()
    {
        Assert.True(IntentCanonicalEncoder.TryEncode(
            Draft(proposed: new IntentTypedValue.QuantityValue(-0.0, "autodesk.unit.unit:meters-1.0.0"), measurable: true),
            out _,
            out _,
            out _,
            out var negativeBytes,
            out var negativeFingerprint));
        Assert.True(IntentCanonicalEncoder.TryEncode(
            Draft(proposed: new IntentTypedValue.QuantityValue(0.0, "autodesk.unit.unit:meters-1.0.0"), measurable: true),
            out _,
            out _,
            out _,
            out var positiveBytes,
            out var positiveFingerprint));

        Assert.Equal(positiveBytes, negativeBytes);
        Assert.Equal(positiveFingerprint, negativeFingerprint);

        var store = Store(new Queue<byte[]>(new[] { Bytes(8) }));
        var created = store.TryCreate(Draft(
            proposed: new IntentTypedValue.QuantityValue(-0.0, "autodesk.unit.unit:meters-1.0.0"),
            measurable: true));
        Assert.Equal(IntentStoreCreateStatus.Created, created.Status);
        Assert.True(store.TryGet(created.IntentRef!, out var stored));
        var quantity = Assert.IsType<IntentTypedValue.QuantityValue>(stored!.Items[0].Proposed);
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(quantity.Value));
        Assert.NotEqual(0L, BitConverter.DoubleToInt64Bits(quantity.Value));
    }

    [Fact]
    public void Retrieved_entry_records_fingerprint_schema_version_one()
    {
        var store = Store(new Queue<byte[]>(new[] { Bytes(1) }));
        var created = store.TryCreate(Draft());

        Assert.True(store.TryGet(created.IntentRef!, out var stored));
        Assert.Equal(1, stored!.FingerprintSchemaVersion);
    }

    [Fact]
    public void Absent_and_present_empty_optional_strings_differ()
    {
        var absent = Draft();
        var presentEmpty = Draft();
        presentEmpty.Items![0].SharedGuid = string.Empty;

        Assert.True(IntentCanonicalEncoder.TryEncode(
            absent,
            out _,
            out _,
            out _,
            out var absentBytes,
            out var absentFingerprint));
        Assert.True(IntentCanonicalEncoder.TryEncode(
            presentEmpty,
            out _,
            out _,
            out _,
            out var presentBytes,
            out var presentFingerprint));

        Assert.NotEqual(absentBytes, presentBytes);
        Assert.NotEqual(absentFingerprint, presentFingerprint);
    }

    [Fact]
    public void Schema_v1_golden_canonical_vector()
    {
        const string expectedCanonical =
            "00000001" +
            "0000000169" +
            "0000000164" +
            "00000001" +
            "00000001" +
            "0000000165" +
            "0000000170" +
            "00000008696e7374616e6365" +
            "000000056c6f63616c" +
            "00" +
            "0100000000" +
            "000000016b" +
            "000000026f6b" +
            "000000016e" +
            "00" +
            "00000000" +
            "00" +
            "000000016d" +
            "01" +
            "0000000473706563" +
            "00" +
            "00" +
            "01000000026162";
        const string expectedFingerprint = "fc3a6571543de582f0ca77364b3ed958049a85a49880da332bacd3934d06ee32";

        var draft = new IntentDraft
        {
            InstanceId = "i",
            DocumentId = "d",
            Items = new List<IntentItemDraft>
            {
                new()
                {
                    RequestPosition = 1,
                    ElementRef = "e",
                    ParameterRef = "p",
                    Source = "instance",
                    IdentityKind = DescribeParameterIdentityKind.Local,
                    ParameterTypeId = null,
                    SharedGuid = string.Empty,
                    StableKey = "k",
                    Status = "ok",
                    ElementName = "n",
                    ElementNameTruncated = false,
                    CategoryName = string.Empty,
                    CategoryNameTruncated = false,
                    ParameterName = "m",
                    ParameterNameTruncated = true,
                    DataTypeKind = DescribeParameterDataTypeKind.Spec,
                    ForgeTypeId = null,
                    BeforeHasValue = false,
                    Proposed = new IntentTypedValue.StringValue("ab")
                }
            }
        };

        Assert.True(IntentCanonicalEncoder.TryEncode(
            draft,
            out _,
            out _,
            out _,
            out var canonical,
            out var fingerprint));
        Assert.Equal(expectedCanonical, IntentCanonicalEncoder.ToLowerHex(canonical));
        Assert.Equal(expectedFingerprint, fingerprint);
    }

    [Fact]
    public void Non_finite_quantity_is_rejected_and_not_stored()
    {
        var store = Store(new Queue<byte[]>(new[] { Bytes(1) }));

        Assert.Equal(IntentStoreCreateStatus.Rejected, store.TryCreate(Draft(proposed: new IntentTypedValue.QuantityValue(double.NaN, "u"), measurable: true)).Status);
        Assert.Equal(IntentStoreCreateStatus.Rejected, store.TryCreate(Draft(proposed: new IntentTypedValue.QuantityValue(double.PositiveInfinity, "u"), measurable: true)).Status);
        Assert.Equal(IntentStoreCreateStatus.Rejected, store.TryCreate(Draft(proposed: new IntentTypedValue.QuantityValue(double.NegativeInfinity, "u"), measurable: true)).Status);
        Assert.Equal(IntentStoreCreateStatus.Created, store.TryCreate(Draft()).Status);
    }

    [Fact]
    public void Monotonic_lifetime_expires_at_ten_minutes_and_ignores_utc_movement()
    {
        var clock = new ManualTimeProvider();
        var store = new EphemeralWriteIntentStore(clock, () => Bytes(3));
        var created = store.TryCreate(Draft());

        Assert.Equal(clock.UtcNow, created.ExpiresAt - EphemeralWriteIntentStore.Lifetime);
        clock.Advance(EphemeralWriteIntentStore.Lifetime - TimeSpan.FromTicks(1));
        Assert.True(store.TryGet(created.IntentRef!, out _));

        clock.UtcNow = clock.UtcNow.AddYears(5);
        Assert.True(store.TryGet(created.IntentRef!, out _));
        clock.UtcNow = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.True(store.TryGet(created.IntentRef!, out _));

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.False(store.TryGet(created.IntentRef!, out _));
    }

    [Fact]
    public void Purge_frees_a_capacity_slot_without_evicting_live_intents()
    {
        var clock = new ManualTimeProvider();
        var next = 0;
        var store = new EphemeralWriteIntentStore(clock, () => Bytes((byte)Interlocked.Increment(ref next)));
        var live = new List<string>();
        for (var index = 0; index < EphemeralWriteIntentStore.LiveCapacity; index++)
        {
            var created = store.TryCreate(Draft(documentId: "doc-live", elementRef: "element-" + index));
            Assert.Equal(IntentStoreCreateStatus.Created, created.Status);
            live.Add(created.IntentRef!);
        }

        var blocked = store.TryCreate(Draft(documentId: "doc-live", elementRef: "overflow"));
        Assert.Equal(IntentStoreCreateStatus.CapacityReached, blocked.Status);
        foreach (var intentRef in live)
        {
            Assert.True(store.TryGet(intentRef, out _));
        }

        clock.Advance(EphemeralWriteIntentStore.Lifetime);
        Assert.Equal(EphemeralWriteIntentStore.LiveCapacity, store.PurgeExpired());
        var freed = store.TryCreate(Draft(documentId: "doc-live", elementRef: "after-purge"));
        Assert.Equal(IntentStoreCreateStatus.Created, freed.Status);
        Assert.False(store.TryGet(live[0], out _));
    }

    [Fact]
    public void ForgetDocument_removes_only_the_matching_document()
    {
        var draws = new Queue<byte[]>(new[] { Bytes(1), Bytes(2), Bytes(3) });
        var store = Store(draws);
        var first = store.TryCreate(Draft(documentId: "doc-a", elementRef: "a1"));
        var second = store.TryCreate(Draft(documentId: "doc-a", elementRef: "a2"));
        var other = store.TryCreate(Draft(documentId: "doc-b", elementRef: "b1"));

        Assert.Equal(2, store.ForgetDocument("doc-a"));
        Assert.False(store.TryGet(first.IntentRef!, out _));
        Assert.False(store.TryGet(second.IntentRef!, out _));
        Assert.True(store.TryGet(other.IntentRef!, out var kept));
        Assert.Equal("local:42", kept!.Items[0].StableKey);
    }

    [Fact]
    public void Clear_removes_every_intent_and_rejects_later_creation()
    {
        var store = Store(new Queue<byte[]>(new[] { Bytes(1), Bytes(2) }));
        var created = store.TryCreate(Draft());

        Assert.Equal(1, store.Clear());
        Assert.False(store.TryGet(created.IntentRef!, out _));
        var rejected = store.TryCreate(Draft());
        Assert.Equal(IntentStoreCreateStatus.Rejected, rejected.Status);
        Assert.NotEqual(IntentStoreCreateStatus.CapacityReached, rejected.Status);
    }

    [Fact]
    public void Concurrent_creates_never_exceed_live_capacity()
    {
        var next = 0;
        var store = new EphemeralWriteIntentStore(
            new ManualTimeProvider(),
            () => Bytes((byte)Interlocked.Increment(ref next)));
        var created = new ConcurrentBag<string>();
        var capacityReached = 0;

        Parallel.For(0, EphemeralWriteIntentStore.LiveCapacity + 32, index =>
        {
            var result = store.TryCreate(Draft(elementRef: "element-" + index));
            if (result.Status == IntentStoreCreateStatus.Created)
            {
                created.Add(result.IntentRef!);
            }
            else
            {
                Assert.Equal(IntentStoreCreateStatus.CapacityReached, result.Status);
                Interlocked.Increment(ref capacityReached);
            }
        });

        Assert.Equal(EphemeralWriteIntentStore.LiveCapacity, created.Count);
        Assert.Equal(32, capacityReached);
        Assert.Equal(created.Count, created.Distinct(StringComparer.Ordinal).Count());
        foreach (var intentRef in created)
        {
            Assert.True(store.TryGet(intentRef, out _));
        }
    }

    [Fact]
    public void Caller_mutation_after_create_does_not_change_the_stored_snapshot()
    {
        var store = Store(new Queue<byte[]>(new[] { Bytes(7) }));
        var draft = Draft();
        var created = store.TryCreate(draft);

        draft.InstanceId = "changed-instance";
        draft.DocumentId = "changed-document";
        draft.Items![0].ElementName = "changed-name";
        draft.Items[0].StableKey = "changed-key";
        draft.Items[0].Proposed = new IntentTypedValue.StringValue("changed");
        draft.Items.Clear();

        Assert.True(store.TryGet(created.IntentRef!, out var stored));
        Assert.Equal("instance-a", stored!.InstanceId);
        Assert.Equal("doc-a", stored.DocumentId);
        Assert.Single(stored.Items);
        Assert.Equal("Wall 1", stored.Items[0].ElementName);
        Assert.Equal("local:42", stored.Items[0].StableKey);
        var proposed = Assert.IsType<IntentTypedValue.StringValue>(stored.Items[0].Proposed);
        Assert.Equal("proposed", proposed.Value);
    }

    [Fact]
    public void Store_exposes_no_listing_search_or_recent_api()
    {
        var names = typeof(EphemeralWriteIntentStore)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain(names, name =>
            name.Contains("List", StringComparison.Ordinal)
            || name.Contains("Search", StringComparison.Ordinal)
            || name.Contains("Recent", StringComparison.Ordinal)
            || name.Contains("Enumerate", StringComparison.Ordinal));
    }

    private static EphemeralWriteIntentStore Store(Queue<byte[]> draws)
    {
        return new EphemeralWriteIntentStore(new ManualTimeProvider(), draws.Dequeue);
    }

    private static string Fingerprint(IntentDraft draft)
    {
        Assert.True(IntentCanonicalEncoder.TryEncode(
            draft,
            out _,
            out _,
            out _,
            out _,
            out var fingerprint));
        return fingerprint;
    }

    private static IntentDraft Draft(
        string documentId = "doc-a",
        string elementRef = "element-a",
        string elementName = "Wall 1",
        string? forgeTypeId = null,
        string? parameterTypeId = null,
        IntentTypedValue? before = null,
        IntentTypedValue? proposed = null,
        bool measurable = false)
    {
        return new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = documentId,
            Items = new List<IntentItemDraft> { Item(1, elementRef, elementName, forgeTypeId, parameterTypeId, before, proposed, measurable) }
        };
    }

    private static IntentDraft TwoItems(string firstRef, string secondRef)
    {
        return new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = "doc-a",
            Items = new List<IntentItemDraft>
            {
                Item(1, firstRef, "Wall 1", null, null, null, null, false),
                Item(2, secondRef, "Door 1", null, null, null, null, false)
            }
        };
    }

    private static IntentItemDraft Item(
        int position,
        string elementRef,
        string elementName,
        string? forgeTypeId,
        string? parameterTypeId,
        IntentTypedValue? before,
        IntentTypedValue? proposed,
        bool measurable)
    {
        return new IntentItemDraft
        {
            RequestPosition = position,
            ElementRef = elementRef,
            ParameterRef = "parameter-a",
            Source = "instance",
            IdentityKind = DescribeParameterIdentityKind.Local,
            ParameterTypeId = parameterTypeId,
            SharedGuid = null,
            StableKey = "local:42",
            Status = "ok",
            ElementName = elementName,
            ElementNameTruncated = false,
            CategoryName = "Walls",
            CategoryNameTruncated = false,
            ParameterName = "Comments",
            ParameterNameTruncated = false,
            DataTypeKind = measurable
                ? DescribeParameterDataTypeKind.MeasurableSpec
                : DescribeParameterDataTypeKind.Spec,
            ForgeTypeId = forgeTypeId,
            BeforeHasValue = before is not null,
            BeforeValue = before,
            Proposed = proposed ?? new IntentTypedValue.StringValue("proposed")
        };
    }

    private static byte[] Bytes(byte marker)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteInt32BigEndian(bytes, marker);
        bytes[4] = marker;
        return bytes;
    }
}
