using System.Text;
using System.Text.Json;
using BimIt.Revit.Addin.Approval;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class MepDevicePlacementIntentTests
{
    [Fact]
    public void Mep_fingerprint_is_deterministic_domain_separated_and_sensitive_to_order_offset_and_host()
    {
        var first = Sample();
        Assert.True(MepDevicePlacementCanonicalEncoder.TryEncode("instance-a", "doc-a", first, out var canonical, out var fingerprint));
        Assert.True(MepDevicePlacementCanonicalEncoder.TryEncode("instance-a", "doc-a", Sample(), out _, out var again));
        Assert.Equal(fingerprint, again);
        Assert.Contains("mep_device_placements", Encoding.UTF8.GetString(canonical), StringComparison.Ordinal);

        var reversed = Sample([Item(1, 2400, 0), Item(2, 1200, 0)]);
        Assert.True(MepDevicePlacementCanonicalEncoder.TryEncode("instance-a", "doc-a", reversed, out _, out var reversedFingerprint));
        Assert.NotEqual(fingerprint, reversedFingerprint);

        var moved = Sample([Item(1, 1201, 0), Item(2, 2400, 0)]);
        Assert.True(MepDevicePlacementCanonicalEncoder.TryEncode("instance-a", "doc-a", moved, out _, out var movedFingerprint));
        Assert.NotEqual(fingerprint, movedFingerprint);

        var otherHost = Sample(hostEnd: new MepPoint(30, 0, 0));
        Assert.True(MepDevicePlacementCanonicalEncoder.TryEncode("instance-a", "doc-a", otherHost, out _, out var hostFingerprint));
        Assert.NotEqual(fingerprint, hostFingerprint);

        var level = Sample(strategy: MepPlacementStrategyKind.LevelBased);
        Assert.True(MepDevicePlacementCanonicalEncoder.TryEncode("instance-a", "doc-a", level, out _, out var levelFingerprint));
        Assert.NotEqual(fingerprint, levelFingerprint);

        var rotated = Sample(rotation: 0.4);
        Assert.True(MepDevicePlacementCanonicalEncoder.TryEncode("instance-a", "doc-a", rotated, out _, out var rotationFingerprint));
        Assert.NotEqual(fingerprint, rotationFingerprint);

        var shiftedY = Sample([Item(1, 1200, 1), Item(2, 2400, 0)]);
        Assert.True(MepDevicePlacementCanonicalEncoder.TryEncode("instance-a", "doc-a", shiftedY, out _, out var yFingerprint));
        Assert.NotEqual(fingerprint, yFingerprint);

        Assert.True(IntentCanonicalEncoder.TryEncode(ParameterDraft(), out _, out _, out _, out var parameterCanonical, out _));
        Assert.DoesNotContain("mep_device_placements", Encoding.UTF8.GetString(parameterCanonical), StringComparison.Ordinal);
    }

    [Fact]
    public void Parameter_and_mep_intents_share_ttl_capacity_and_document_close()
    {
        var clock = new ManualTimeProvider();
        var store = new EphemeralWriteIntentStore(clock);
        var parameter = store.TryCreate(ParameterDraft());
        var mep = store.TryCreateMep("instance-a", "doc-a", Sample());
        Assert.Equal(IntentStoreCreateStatus.Created, parameter.Status);
        Assert.Equal(IntentStoreCreateStatus.Created, mep.Status);
        Assert.True(store.TryGet(parameter.IntentRef!, out _));
        Assert.False(store.TryGet(mep.IntentRef!, out _));
        Assert.True(store.TryGetControlled(mep.IntentRef!, out var controlled));
        Assert.Equal(ControlledWriteIntentKind.MepDevicePlacements, controlled!.Binding.Kind);
        Assert.Equal(clock.UtcNow.AddMinutes(10), parameter.ExpiresAt);
        Assert.Equal(clock.UtcNow.AddMinutes(10), mep.ExpiresAt);

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(store.TryGetControlled(parameter.IntentRef!, out _));
        Assert.False(store.TryGetControlled(mep.IntentRef!, out _));
    }

    [Fact]
    public void Shared_capacity_counts_both_kinds()
    {
        var sequence = 0;
        var store = new EphemeralWriteIntentStore(new ManualTimeProvider(), () =>
        {
            var bytes = new byte[32];
            bytes[0] = (byte)sequence++;
            return bytes;
        });
        for (var index = 0; index < 63; index++)
        {
            Assert.Equal(IntentStoreCreateStatus.Created, store.TryCreate(ParameterDraft(documentId: "doc-" + index)).Status);
        }

        var mep = store.TryCreateMep("instance-a", "doc-shared", Sample());
        Assert.Equal(IntentStoreCreateStatus.Created, mep.Status);
        Assert.Equal(IntentStoreCreateStatus.CapacityReached, store.TryCreate(ParameterDraft(documentId: "doc-extra")).Status);
        Assert.Equal(1, store.ForgetDocument("doc-shared"));
        Assert.False(store.TryGetControlled(mep.IntentRef!, out _));
    }

    [Fact]
    public void Document_close_clears_both_kinds()
    {
        var store = new EphemeralWriteIntentStore();
        var parameter = store.TryCreate(ParameterDraft());
        var mep = store.TryCreateMep("instance-a", "doc-a", Sample());
        Assert.Equal(2, store.ForgetDocument("doc-a"));
        Assert.False(store.TryGet(parameter.IntentRef!, out _));
        Assert.False(store.TryGetControlled(mep.IntentRef!, out _));
    }

    [Fact]
    public void Approval_binds_intent_kind_and_cannot_cross_authorize()
    {
        var store = new EphemeralWriteIntentStore();
        var parameter = store.TryCreate(ParameterDraft());
        var mep = store.TryCreateMep("instance-a", "doc-a", Sample());
        var provider = new RevitLocalApprovalProviderStateMachine(store, sessionRefFactory: () => "session-a");

        var parameterReview = provider.BeginReview(parameter.IntentRef, "doc-a", ControlledWriteIntentKind.MepDevicePlacements);
        Assert.Equal(ApprovalReviewStatus.Unavailable, parameterReview.Status);
        var mepReview = provider.BeginReview(mep.IntentRef, "doc-a", ControlledWriteIntentKind.ParameterUpdates);
        Assert.Equal(ApprovalReviewStatus.Unavailable, mepReview.Status);

        var started = provider.BeginReview(mep.IntentRef, "doc-a", ControlledWriteIntentKind.MepDevicePlacements);
        Assert.Equal(ApprovalReviewStatus.Started, started.Status);
        Assert.Equal(ApprovalCommandStatus.Recorded, provider.ApproveCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, provider.TryConsumeApproved(mep.IntentRef).Status);
        var consumed = provider.TryConsumeApproved(mep.IntentRef, ControlledWriteIntentKind.MepDevicePlacements);
        Assert.Equal(ApprovalConsumeStatus.Consumed, consumed.Status);
        Assert.True(consumed.Snapshot.HasValue);
        Assert.Equal(ControlledWriteIntentKind.MepDevicePlacements, consumed.Snapshot.Value.IntentKind);

        var parameterStarted = provider.BeginReview(parameter.IntentRef, "doc-a");
        Assert.Equal(ApprovalCommandStatus.Recorded, provider.ApproveCurrent(parameterStarted.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, provider.TryConsumeApproved(parameter.IntentRef, ControlledWriteIntentKind.MepDevicePlacements).Status);
    }

    [Fact]
    public void Mep_render_contains_authoritative_offsets_and_parameter_render_stays_item_based()
    {
        var store = new EphemeralWriteIntentStore(new ManualTimeProvider());
        var created = store.TryCreateMep("instance-a", "doc-a", Sample());
        Assert.True(store.TryGetControlled(created.IntentRef!, out var stored));
        Assert.True(ApprovalReviewRenderer.TryCreateMep("session-a", stored!.Binding, stored.MepDevicePlacements!, out var model));
        var json = ApprovalReviewJson.SerializeRender(model);
        Assert.Contains("\"reviewKind\":\"mep_device_placements\"", json, StringComparison.Ordinal);
        Assert.Contains("Create MEP devices", json, StringComparison.Ordinal);
        Assert.Contains("Electrical Fixtures", json, StringComparison.Ordinal);
        Assert.Contains("Duplex Receptacle", json, StringComparison.Ordinal);
        Assert.Contains("Basic Wall", json, StringComparison.Ordinal);
        using var rendered = JsonDocument.Parse(json);
        Assert.Equal("Wall hosted", rendered.RootElement.GetProperty("placementLabel").GetString());
        Assert.Equal("1. X +1200 mm, Y +0 mm, vertical +0 mm", rendered.RootElement.GetProperty("placementLines")[0].GetString());
        Assert.Equal("2. X +2400 mm, Y +0 mm, vertical +0 mm", rendered.RootElement.GetProperty("placementLines")[1].GetString());
        Assert.DoesNotContain("templateElementRef", json, StringComparison.Ordinal);
        Assert.DoesNotContain(stored.MepDevicePlacements!.TemplateElementRef, json, StringComparison.Ordinal);

        var parameter = store.TryCreate(ParameterDraft());
        Assert.True(store.TryGet(parameter.IntentRef!, out var entry));
        Assert.True(ApprovalReviewRenderer.TryCreate("session-b", entry!, out var parameterModel));
        var parameterJson = ApprovalReviewJson.SerializeRender(parameterModel);
        Assert.Contains("\"items\":", parameterJson, StringComparison.Ordinal);
        Assert.DoesNotContain("reviewKind", parameterJson, StringComparison.Ordinal);
    }

    private static IntentDraft ParameterDraft(string documentId = "doc-a")
    {
        return new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = documentId,
            Items =
            [
                new IntentItemDraft
                {
                    RequestPosition = 1,
                    ElementRef = "element-a",
                    ParameterRef = "parameter-a",
                    Source = "instance",
                    IdentityKind = DescribeParameterIdentityKind.Local,
                    StableKey = "local:42",
                    Status = "ok",
                    ElementName = "Wall",
                    CategoryName = "Walls",
                    ParameterName = "Comments",
                    DataTypeKind = DescribeParameterDataTypeKind.Spec,
                    BeforeHasValue = true,
                    BeforeValue = new IntentTypedValue.StringValue("before"),
                    Proposed = new IntentTypedValue.StringValue("after")
                }
            ]
        };
    }

    private static MepDevicePlacementIntent Sample(
        IReadOnlyList<MepDevicePlacementItem>? items = null,
        MepPoint? hostEnd = null,
        MepPlacementStrategyKind strategy = MepPlacementStrategyKind.WallHosted,
        double rotation = 0)
    {
        var stored = items ?? [Item(1, 1200, 0), Item(2, 2400, 0)];
        return new MepDevicePlacementIntent(
            strategy,
            "autodesk.unit.unit:millimeters-1.0.1",
            "template-a",
            "symbol-a",
            "OST_ElectricalFixtures",
            "Electrical Fixtures",
            "Duplex Receptacle",
            "Standard",
            "level-a",
            "L4",
            new MepPoint(1, 0, 4),
            new MepOrientation(rotation, false, false, true, true),
            strategy == MepPlacementStrategyKind.WallHosted
                ? new MepWallBinding("wall-a", "Basic Wall", new MepPoint(0, 0, 0), hostEnd ?? new MepPoint(20, 0, 0))
                : null,
            strategy == MepPlacementStrategyKind.WorkPlaneBasedLevel ? new MepPlanDirection(1, 0) : null,
            stored);
    }

    private static MepDevicePlacementItem Item(int position, double offsetX, double offsetY = 0, double vertical = 0)
    {
        return new MepDevicePlacementItem(position, offsetX, offsetY, vertical, new MepPoint(offsetX, offsetY, vertical));
    }
}
