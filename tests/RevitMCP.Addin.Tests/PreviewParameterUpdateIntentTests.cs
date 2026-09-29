using System.Buffers.Binary;
using RevitMCP.Addin.Inspection;
using RevitMCP.Addin.Intents;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class PreviewParameterUpdateIntentTests
{
    [Fact]
    public void Intent_draft_preserves_request_order_and_exact_typed_values()
    {
        var firstProposed = new PreviewParameterStringValue { Value = " next " };
        var secondProposed = new PreviewParameterIntegerValue { Value = int.MinValue };
        var draft = PreviewParameterUpdateResults.Compose("instance-a", "doc-a",
        [
            Candidate("element-b", "parameter-b", firstProposed, new PreviewParameterStringValue { Value = "old" }),
            Candidate("element-a", "parameter-a", secondProposed, new PreviewParameterIntegerValue { Value = 4 }, DescribeParameterIdentityKind.BuiltIn, "autodesk.parameter:comments")
        ]);

        Assert.Equal("instance-a", draft.InstanceId);
        Assert.Equal("doc-a", draft.DocumentId);
        Assert.Equal(2, draft.Items!.Count);
        Assert.Equal(1, draft.Items[0].RequestPosition);
        Assert.Equal("element-b", draft.Items[0].ElementRef);
        Assert.Equal("parameter-b", draft.Items[0].ParameterRef);
        Assert.Equal("instance", draft.Items[0].Source);
        Assert.Equal("ok", draft.Items[0].Status);
        var first = Assert.IsType<IntentTypedValue.StringValue>(draft.Items[0].Proposed);
        Assert.Equal(" next ", first.Value);
        var firstBefore = Assert.IsType<IntentTypedValue.StringValue>(draft.Items[0].BeforeValue);
        Assert.Equal("old", firstBefore.Value);

        Assert.Equal(2, draft.Items[1].RequestPosition);
        Assert.Equal("element-a", draft.Items[1].ElementRef);
        Assert.Equal(DescribeParameterIdentityKind.BuiltIn, draft.Items[1].IdentityKind);
        Assert.Equal("autodesk.parameter:comments", draft.Items[1].ParameterTypeId);
        Assert.Equal("local:42", draft.Items[1].StableKey);
        var second = Assert.IsType<IntentTypedValue.IntegerValue>(draft.Items[1].Proposed);
        Assert.Equal(int.MinValue, second.Value);
    }

    [Fact]
    public void Negative_zero_proposed_quantity_is_preserved_in_the_intent_draft()
    {
        var proposed = new PreviewParameterQuantityValue
        {
            Value = -0.0,
            UnitTypeId = "autodesk.unit.unit:meters-1.0.0"
        };
        var draft = PreviewParameterUpdateResults.Compose("instance-a", "doc-a",
        [
            Candidate(
                "element-a",
                "parameter-a",
                proposed,
                new PreviewParameterQuantityValue { Value = 1.5, UnitTypeId = proposed.UnitTypeId },
                measurable: true)
        ]);

        var stored = Assert.IsType<IntentTypedValue.QuantityValue>(draft.Items![0].Proposed);
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(stored.Value));
        Assert.Equal(proposed.UnitTypeId, stored.UnitTypeId);
        Assert.NotEqual(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(stored.Value));
    }

    [Fact]
    public void Created_capacity_and_rejected_store_results_map_to_the_preview_contract()
    {
        var clock = new ManualTimeProvider();
        var next = 0;
        var store = new EphemeralWriteIntentStore(clock, () => Bytes((byte)Interlocked.Increment(ref next)));
        var created = PreviewParameterUpdateResults.Complete(Context(), [OkCandidate()], store.TryCreate);

        Assert.True(created.Ready);
        Assert.Equal(IntentCanonicalEncoder.ToLowerHex(Bytes(1)), created.IntentRef);
        Assert.False(string.IsNullOrEmpty(created.IntentFingerprint));
        Assert.Equal(clock.UtcNow + EphemeralWriteIntentStore.Lifetime, created.ExpiresAt);
        Assert.True(store.TryGet(created.IntentRef!, out var stored));
        var proposed = Assert.IsType<IntentTypedValue.StringValue>(stored!.Items[0].Proposed);
        Assert.Equal("next", proposed.Value);

        for (var index = 0; index < EphemeralWriteIntentStore.LiveCapacity - 1; index++)
        {
            Assert.Equal(
                IntentStoreCreateStatus.Created,
                store.TryCreate(PreviewParameterUpdateResults.Compose("instance-a", "doc-a", [OkCandidate($"element-{index}")])).Status);
        }

        var capacity = Assert.Throws<BridgeException>(() =>
            PreviewParameterUpdateResults.Complete(Context(), [OkCandidate("overflow")], store.TryCreate));
        Assert.Equal(CapabilityErrorCodes.IntentCapacityReached, capacity.ErrorCode);

        store.Clear();
        var rejected = Assert.Throws<BridgeException>(() =>
            PreviewParameterUpdateResults.Complete(Context(), [OkCandidate()], store.TryCreate));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, rejected.ErrorCode);
    }

    [Fact]
    public void Ready_false_never_creates_an_intent()
    {
        var called = 0;
        var result = PreviewParameterUpdateResults.Complete(
            Context(),
            [OkCandidate(), Candidate("element-b", "parameter-b", new PreviewParameterStringValue { Value = "same" }, new PreviewParameterStringValue { Value = "same" }, status: PreviewParameterUpdateStatus.NoChange)],
            _ =>
            {
                called++;
                throw new InvalidOperationException("Intent creation must not run.");
            });

        Assert.Equal(0, called);
        Assert.False(result.Ready);
        Assert.Null(result.IntentRef);
        Assert.Null(result.IntentFingerprint);
        Assert.Null(result.ExpiresAt);
        Assert.Equal(PreviewParameterUpdateStatus.NoChange, result.Items[1].Status);
        Assert.Equal(["element_ref", "parameter_ref", "status"], PublicNames(PreviewParameterUpdateResults.ToPublicItem(
            new PreviewParameterUpdateCandidate
            {
                ElementRef = "el",
                ParameterRef = "pr",
                Status = PreviewParameterUpdateStatus.ElementNotFound
            })));
    }

    private static string[] PublicNames(PreviewParameterUpdateItem item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item, ContractJson.Options);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }

    private static DescribeParametersContext Context()
        => new() { InstanceId = "instance-a", DocumentId = "doc-a" };

    private static PreviewParameterUpdateCandidate OkCandidate(string elementRef = "element-a")
        => Candidate(elementRef, "parameter-a", new PreviewParameterStringValue { Value = "next" }, new PreviewParameterStringValue { Value = "old" });

    private static PreviewParameterUpdateCandidate Candidate(
        string elementRef,
        string parameterRef,
        PreviewParameterValue proposed,
        PreviewParameterValue? before,
        DescribeParameterIdentityKind identityKind = DescribeParameterIdentityKind.Local,
        string? parameterTypeId = null,
        bool measurable = false,
        PreviewParameterUpdateStatus status = PreviewParameterUpdateStatus.Ok)
    {
        return new PreviewParameterUpdateCandidate
        {
            ElementRef = elementRef,
            ParameterRef = parameterRef,
            Status = status,
            ElementName = "Wall 1",
            ElementNameTruncated = false,
            CategoryName = "Walls",
            CategoryNameTruncated = false,
            ParameterName = "Comments",
            ParameterNameTruncated = false,
            DataType = new DescribeParameterDataType
            {
                Kind = measurable ? DescribeParameterDataTypeKind.MeasurableSpec : DescribeParameterDataTypeKind.Spec,
                ForgeTypeId = measurable ? "autodesk.spec.aec:length-2.0.0" : null
            },
            BeforeHasValue = before is not null,
            BeforeValue = before,
            Proposed = proposed,
            IdentityKind = identityKind,
            ParameterTypeId = parameterTypeId,
            StableKey = "local:42"
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
