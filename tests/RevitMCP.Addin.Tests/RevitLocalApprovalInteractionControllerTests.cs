using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Intents;
using RevitMCP.Addin.Lifecycle;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class RevitLocalApprovalInteractionControllerTests
{
    [Fact]
    public void Unknown_intent_returns_unavailable_without_a_render_model()
    {
        var harness = Harness();

        var review = harness.Controller.BeginReview("missing-intent", "doc-a");

        Assert.Equal(ApprovalReviewStatus.Unavailable, review.Status);
        Assert.Null(review.RenderModel);
    }

    [Fact]
    public void Wrong_active_document_returns_unavailable_without_a_render_model()
    {
        var harness = Harness();

        var review = harness.Controller.BeginReview(harness.IntentRef, "doc-b");

        Assert.Equal(ApprovalReviewStatus.Unavailable, review.Status);
        Assert.Null(review.RenderModel);
        Assert.True(harness.Store.TryGet(harness.IntentRef, out _));
    }

    [Fact]
    public void Valid_begin_returns_started_and_a_render_model()
    {
        var harness = Harness();

        var review = harness.Controller.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Started, review.Status);
        Assert.NotNull(review.RenderModel);
        Assert.False(string.IsNullOrWhiteSpace(review.RenderModel.SessionRef));
    }

    [Fact]
    public void Repeated_begin_returns_already_active_and_the_same_session()
    {
        var harness = Harness();
        var started = harness.Controller.BeginReview(harness.IntentRef, "doc-a");

        var again = harness.Controller.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.AlreadyActive, again.Status);
        Assert.Equal(started.RenderModel!.SessionRef, again.RenderModel!.SessionRef);
    }

    [Fact]
    public void Render_model_is_built_from_the_stored_intent()
    {
        var harness = Harness();
        Assert.True(harness.Store.TryGet(harness.IntentRef, out var stored));

        var review = harness.Controller.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(stored!.CreatedAt, review.RenderModel!.CreatedAt);
        Assert.Equal(stored.ExpiresAt, review.RenderModel.ExpiresAt);
        Assert.Equal(stored.Items[0].ElementName, review.RenderModel.Items[0].ElementName);
        Assert.Equal(stored.Items[0].ParameterName, review.RenderModel.Items[0].ParameterName);
    }

    [Fact]
    public void Stored_item_order_is_preserved()
    {
        var harness = Harness(OrderedDraft());

        var items = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.Items;

        Assert.Equal(new[] { 1, 2, 3 }, items.Select(item => item.RequestPosition).ToArray());
        Assert.Equal(new[] { "First", "Second", "Third" }, items.Select(item => item.ElementName).ToArray());
    }

    [Fact]
    public void Render_model_contains_element_category_and_parameter_display_data()
    {
        var harness = Harness();

        var item = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.Items[0];

        Assert.Equal("Wall 1", item.ElementName);
        Assert.False(item.ElementNameTruncated);
        Assert.Equal("Walls", item.CategoryName);
        Assert.False(item.CategoryNameTruncated);
        Assert.Equal("Comments", item.ParameterName);
        Assert.False(item.ParameterNameTruncated);
        Assert.Equal(DescribeParameterDataTypeKind.Spec, item.DataType.Kind);
    }

    [Fact]
    public void Before_no_value_is_preserved_exactly()
    {
        var harness = Harness();

        var before = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.Items[0].Before;

        Assert.False(before.HasValue);
        Assert.Null(before.Value);
    }

    [Fact]
    public void Before_string_value_is_preserved()
    {
        var harness = Harness(Draft(beforeHasValue: true, before: new IntentTypedValue.StringValue("existing")));

        var before = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.Items[0].Before;

        Assert.True(before.HasValue);
        var text = Assert.IsType<ApprovalReviewValueModel.StringValue>(before.Value);
        Assert.Equal("existing", text.Value);
    }

    [Fact]
    public void Before_integer_value_is_preserved()
    {
        var harness = Harness(Draft(
            beforeHasValue: true,
            before: new IntentTypedValue.IntegerValue(7),
            proposed: new IntentTypedValue.IntegerValue(8),
            kind: DescribeParameterDataTypeKind.Spec));

        var before = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.Items[0].Before;

        var integer = Assert.IsType<ApprovalReviewValueModel.IntegerValue>(before.Value);
        Assert.Equal(7, integer.Value);
    }

    [Fact]
    public void Quantity_value_and_unit_type_id_are_preserved()
    {
        var harness = Harness(Draft(
            beforeHasValue: true,
            before: new IntentTypedValue.QuantityValue(12.5, "autodesk.unit.unit:feet-1.0.0"),
            proposed: new IntentTypedValue.QuantityValue(13.5, "autodesk.unit.unit:feet-1.0.0"),
            kind: DescribeParameterDataTypeKind.MeasurableSpec,
            forgeTypeId: "autodesk.spec.aec:length-2.0.0"));

        var before = Assert.IsType<ApprovalReviewValueModel.QuantityValue>(
            harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.Items[0].Before.Value);

        Assert.Equal(12.5, before.Value);
        Assert.Equal("autodesk.unit.unit:feet-1.0.0", before.UnitTypeId);
    }

    [Fact]
    public void Proposed_string_integer_and_quantity_values_are_preserved()
    {
        var harness = Harness(OrderedDraft());

        var items = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.Items;

        Assert.Equal("next", Assert.IsType<ApprovalReviewValueModel.StringValue>(items[0].Proposed).Value);
        Assert.Equal(4, Assert.IsType<ApprovalReviewValueModel.IntegerValue>(items[1].Proposed).Value);
        var quantity = Assert.IsType<ApprovalReviewValueModel.QuantityValue>(items[2].Proposed);
        Assert.Equal(9.25, quantity.Value);
        Assert.Equal("autodesk.unit.unit:cubicFeetPerMinute-1.0.1", quantity.UnitTypeId);
    }

    [Theory]
    [InlineData("IntentRef")]
    [InlineData("IntentFingerprint")]
    [InlineData("InstanceId")]
    [InlineData("DocumentId")]
    public void Render_model_does_not_expose_authority_identifiers(string propertyName)
    {
        foreach (var type in RenderTypes())
        {
            Assert.DoesNotContain(
                type.GetProperties(BindingFlags.Instance | BindingFlags.Public),
                property => string.Equals(property.Name, propertyName, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Render_model_collection_cannot_be_mutated()
    {
        var harness = Harness();
        var items = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.Items;

        var list = Assert.IsAssignableFrom<IList>(items);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(items[0]));
    }

    [Fact]
    public void Expired_intent_cannot_render()
    {
        var harness = Harness();
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        var review = harness.Controller.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Unavailable, review.Status);
        Assert.Null(review.RenderModel);
    }

    [Fact]
    public void Intent_disappearing_after_begin_review_fails_closed_and_leaves_no_actionable_session()
    {
        var clock = new ExpireAfterCreateClock();
        var store = new EphemeralWriteIntentStore(clock, () => Bytes(1));
        var provider = new RevitLocalApprovalProviderStateMachine(store, clock, () => "session");
        var controller = new RevitLocalApprovalInteractionController(store, provider);
        var created = store.TryCreate(Draft());

        var review = controller.BeginReview(created.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Unavailable, review.Status);
        Assert.Null(review.RenderModel);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, controller.ApproveCurrent("session~1", "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, provider.TryConsumeApproved(created.IntentRef).Status);
    }

    [Fact]
    public void Unsupported_fingerprint_schema_fails_closed()
    {
        var harness = Harness();
        ReplaceStored(harness, Entry(harness, schemaVersion: 2));

        AssertFailClosed(harness);
    }

    [Fact]
    public void Empty_intent_fails_closed()
    {
        var harness = Harness(Draft(items: new List<IntentItemDraft>()));

        AssertFailClosed(harness);
    }

    [Fact]
    public void Non_ready_item_status_fails_closed()
    {
        var harness = Harness();
        ReplaceStored(harness, Entry(harness, items: new[] { Item(status: "no_change") }));

        AssertFailClosed(harness);
    }

    [Fact]
    public void Inconsistent_before_state_fails_closed()
    {
        var harness = Harness();
        ReplaceStored(harness, Entry(harness, items: new[] { Item(beforeHasValue: true, before: null) }));

        AssertFailClosed(harness);
    }

    [Fact]
    public void Invalid_quantity_fails_closed()
    {
        var harness = Harness();
        ReplaceStored(
            harness,
            Entry(harness, items: new[]
            {
                Item(proposed: new IntentTypedValue.QuantityValue(double.NaN, "autodesk.unit.unit:feet-1.0.0"))
            }));

        AssertFailClosed(harness);
    }

    [Fact]
    public void Approve_delegates_only_for_the_current_session()
    {
        var harness = Harness();
        var sessionRef = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.SessionRef;

        var recorded = harness.Controller.ApproveCurrent(sessionRef, "doc-a");

        Assert.Equal(ApprovalCommandStatus.Recorded, recorded.Status);
        Assert.Equal(ApprovalConsumeStatus.Consumed, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.ApproveCurrent(sessionRef, "doc-a").Status);
    }

    [Fact]
    public void Reject_delegates_only_for_the_current_session()
    {
        var harness = Harness();
        var sessionRef = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.SessionRef;

        var recorded = harness.Controller.RejectCurrent(sessionRef, "doc-a");

        Assert.Equal(ApprovalCommandStatus.Recorded, recorded.Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.RejectCurrent(sessionRef, "doc-a").Status);
    }

    [Fact]
    public void Stale_session_ref_cannot_decide()
    {
        var harness = Harness();
        var first = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.SessionRef;
        Assert.Equal(ApprovalDismissStatus.Dismissed, harness.Controller.DismissCurrent(first).Status);
        var second = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.SessionRef;

        Assert.NotEqual(first, second);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.ApproveCurrent(first, "doc-a").Status);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.RejectCurrent(first, "doc-a").Status);
    }

    [Fact]
    public void Dismiss_records_no_decision()
    {
        var harness = Harness();
        var sessionRef = harness.Controller.BeginReview(harness.IntentRef, "doc-a").RenderModel!.SessionRef;

        Assert.Equal(ApprovalDismissStatus.Dismissed, harness.Controller.DismissCurrent(sessionRef).Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Equal(ApprovalReviewStatus.Started, harness.Controller.BeginReview(harness.IntentRef, "doc-a").Status);
    }

    [Fact]
    public void Controller_exposes_no_approval_consumption_operation()
    {
        var names = typeof(RevitLocalApprovalInteractionController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(method => method.Name);

        Assert.DoesNotContain(names, name => name.Contains("Consume", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lifetime_owns_one_controller_using_the_same_provider_and_store()
    {
        var clock = new ManualTimeProvider();
        var store = new EphemeralWriteIntentStore(clock, () => Bytes(9));
        var created = store.TryCreate(Draft());
        var lifetime = new RevitExecutionDispatcherLifetime(store, () => { }, () => { });

        var review = lifetime.ApprovalInteraction.BeginReview(created.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Started, review.Status);
        Assert.Equal(
            review.RenderModel!.SessionRef,
            lifetime.ApprovalProvider.BeginReview(created.IntentRef, "doc-a").SessionRef);
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Lifecycle", "RevitLifecycleAdapters.cs"));
        Assert.Equal(2, Count(source, "new RevitLocalApprovalProviderStateMachine(intentStore)"));
        Assert.Equal(2, Count(source, "new RevitLocalApprovalInteractionController(intentStore, _approval)"));
        Assert.Equal(1, Count(source, "new EphemeralWriteIntentStore()"));
    }

    [Fact]
    public void Stopped_provider_makes_interaction_unavailable()
    {
        var harness = Harness();
        harness.Provider.Stop();

        var review = harness.Controller.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Unavailable, review.Status);
        Assert.Null(review.RenderModel);
        Assert.True(harness.Store.TryGet(harness.IntentRef, out _));
    }

    [Fact]
    public void Controller_and_render_model_types_do_not_retain_revit_api_wrappers()
    {
        foreach (var type in new[]
                 {
                     typeof(RevitLocalApprovalInteractionController),
                     typeof(ApprovalReviewRenderModel),
                     typeof(ApprovalReviewItemModel),
                     typeof(ApprovalReviewBeforeModel),
                     typeof(ApprovalReviewDataTypeModel),
                     typeof(ApprovalReviewValueModel)
                 }.Concat(typeof(ApprovalReviewValueModel).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var name = field.FieldType.FullName ?? string.Empty;
                Assert.DoesNotContain("Autodesk.Revit", name, StringComparison.Ordinal);
            }
        }

        var approvalDirectory = Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Approval");
        foreach (var file in new[] { "RevitLocalApprovalInteractionController.cs", "ApprovalReviewRenderModel.cs" })
        {
            var source = File.ReadAllText(Path.Combine(approvalDirectory, file));
            Assert.DoesNotContain("Autodesk.Revit", source, StringComparison.Ordinal);
        }
    }

    private static void AssertFailClosed(HarnessState harness)
    {
        var review = harness.Controller.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Unavailable, review.Status);
        Assert.Null(review.RenderModel);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.ApproveCurrent("session~1", "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    private static void ReplaceStored(HarnessState harness, IntentEntry replacement)
    {
        var entries = (IDictionary)typeof(EphemeralWriteIntentStore)
            .GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(harness.Store)!;
        var storedType = typeof(EphemeralWriteIntentStore).GetNestedType("StoredIntent", BindingFlags.NonPublic)!;
        var stored = Activator.CreateInstance(
            storedType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object?[]
            {
                replacement.IntentRef,
                replacement.IntentFingerprint,
                replacement.FingerprintSchemaVersion,
                replacement.CreatedAt,
                replacement.ExpiresAt,
                0L,
                replacement.InstanceId,
                replacement.DocumentId,
                replacement.Items
            },
            culture: null);
        entries[replacement.IntentRef] = stored;
    }

    private static IntentEntry Entry(HarnessState harness, int schemaVersion = 1, IReadOnlyList<IntentItemEntry>? items = null)
    {
        Assert.True(harness.Store.TryGet(harness.IntentRef, out var stored));
        return new IntentEntry(
            stored!.IntentRef,
            stored.IntentFingerprint,
            schemaVersion,
            stored.CreatedAt,
            stored.ExpiresAt,
            stored.InstanceId,
            stored.DocumentId,
            items ?? stored.Items);
    }

    private static IntentItemEntry Item(
        string status = "ok",
        bool beforeHasValue = false,
        IntentTypedValue? before = null,
        IntentTypedValue? proposed = null)
    {
        return new IntentItemEntry(
            1,
            "element-a",
            "parameter-a",
            "instance",
            DescribeParameterIdentityKind.Local,
            null,
            null,
            "local:42",
            status,
            "Wall 1",
            false,
            "Walls",
            false,
            "Comments",
            false,
            DescribeParameterDataTypeKind.Spec,
            null,
            beforeHasValue,
            before,
            proposed ?? new IntentTypedValue.StringValue("proposed"));
    }

    private static IEnumerable<Type> RenderTypes()
    {
        yield return typeof(ApprovalReviewRenderModel);
        yield return typeof(ApprovalReviewItemModel);
        yield return typeof(ApprovalReviewBeforeModel);
        yield return typeof(ApprovalReviewDataTypeModel);
        yield return typeof(ApprovalReviewValueModel);
        foreach (var nested in typeof(ApprovalReviewValueModel).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return nested;
        }
    }

    private static HarnessState Harness(IntentDraft? draft = null)
    {
        var clock = new ManualTimeProvider();
        var store = new EphemeralWriteIntentStore(clock, () => Bytes(1));
        var created = store.TryCreate(draft ?? Draft());
        Assert.Equal(IntentStoreCreateStatus.Created, created.Status);
        var provider = new RevitLocalApprovalProviderStateMachine(store, clock, () => "session");
        return new HarnessState(store, provider, new RevitLocalApprovalInteractionController(store, provider), clock, created.IntentRef!);
    }

    private static IntentDraft Draft(
        bool beforeHasValue = false,
        IntentTypedValue? before = null,
        IntentTypedValue? proposed = null,
        DescribeParameterDataTypeKind kind = DescribeParameterDataTypeKind.Spec,
        string? forgeTypeId = null,
        List<IntentItemDraft>? items = null)
    {
        return new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = "doc-a",
            Items = items ?? new List<IntentItemDraft>
            {
                new()
                {
                    RequestPosition = 1,
                    ElementRef = "element-a",
                    ParameterRef = "parameter-a",
                    Source = "instance",
                    IdentityKind = DescribeParameterIdentityKind.Local,
                    StableKey = "local:42",
                    Status = "ok",
                    ElementName = "Wall 1",
                    CategoryName = "Walls",
                    ParameterName = "Comments",
                    DataTypeKind = kind,
                    ForgeTypeId = forgeTypeId,
                    BeforeHasValue = beforeHasValue,
                    BeforeValue = before,
                    Proposed = proposed ?? new IntentTypedValue.StringValue("proposed")
                }
            }
        };
    }

    private static IntentDraft OrderedDraft()
    {
        return new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = "doc-a",
            Items = new List<IntentItemDraft>
            {
                ItemDraft(1, "First", "Comments", DescribeParameterDataTypeKind.Spec, new IntentTypedValue.StringValue("next")),
                ItemDraft(2, "Second", "Mark", DescribeParameterDataTypeKind.Spec, new IntentTypedValue.IntegerValue(4), new IntentTypedValue.IntegerValue(3)),
                ItemDraft(
                    3,
                    "Third",
                    "Flow",
                    DescribeParameterDataTypeKind.MeasurableSpec,
                    new IntentTypedValue.QuantityValue(9.25, "autodesk.unit.unit:cubicFeetPerMinute-1.0.1"),
                    new IntentTypedValue.QuantityValue(8.0, "autodesk.unit.unit:cubicFeetPerMinute-1.0.1"),
                    "autodesk.spec.aec.hvac:airFlow-2.0.0")
            }
        };
    }

    private static IntentItemDraft ItemDraft(
        int position,
        string elementName,
        string parameterName,
        DescribeParameterDataTypeKind kind,
        IntentTypedValue proposed,
        IntentTypedValue? before = null,
        string? forgeTypeId = null)
    {
        return new IntentItemDraft
        {
            RequestPosition = position,
            ElementRef = "element-" + position.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ParameterRef = "parameter-" + position.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Source = "instance",
            IdentityKind = DescribeParameterIdentityKind.Local,
            StableKey = "local:" + position.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Status = "ok",
            ElementName = elementName,
            CategoryName = "Walls",
            ParameterName = parameterName,
            DataTypeKind = kind,
            ForgeTypeId = forgeTypeId,
            BeforeHasValue = before is not null,
            BeforeValue = before,
            Proposed = proposed
        };
    }

    private static byte[] Bytes(byte marker)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteInt32BigEndian(bytes, marker);
        bytes[4] = marker;
        return bytes;
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RevitMCP.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class ExpireAfterCreateClock : TimeProvider
    {
        private int _reads;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        public override long GetTimestamp()
        {
            _reads++;
            return _reads >= 3 ? TimeSpan.FromMinutes(10).Ticks : 0;
        }
    }

    private sealed record HarnessState(
        EphemeralWriteIntentStore Store,
        RevitLocalApprovalProviderStateMachine Provider,
        RevitLocalApprovalInteractionController Controller,
        ManualTimeProvider Clock,
        string IntentRef);
}
