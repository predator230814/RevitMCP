using System.Text.Json;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Contracts.Tests;

public sealed class PreviewMepDevicePlacementsContractTests
{
    [Theory]
    [InlineData(PreviewMepDeviceTemplateStatus.Ok, "ok")]
    [InlineData(PreviewMepDeviceTemplateStatus.NotFound, "not_found")]
    [InlineData(PreviewMepDeviceTemplateStatus.NotFamilyInstance, "not_family_instance")]
    [InlineData(PreviewMepDeviceTemplateStatus.UnsupportedCategory, "unsupported_category")]
    [InlineData(PreviewMepDeviceTemplateStatus.UnsupportedPlacement, "unsupported_placement")]
    [InlineData(PreviewMepDeviceTemplateStatus.UnsupportedHost, "unsupported_host")]
    [InlineData(PreviewMepDeviceTemplateStatus.UnsupportedHostGeometry, "unsupported_host_geometry")]
    [InlineData(PreviewMepDeviceTemplateStatus.UnsupportedLocation, "unsupported_location")]
    [InlineData(PreviewMepDeviceTemplateStatus.UnsupportedLevel, "unsupported_level")]
    public void Template_statuses_serialize_as_closed_snake_case(PreviewMepDeviceTemplateStatus status, string expected)
    {
        Assert.Equal("\"" + expected + "\"", JsonSerializer.Serialize(status, ContractJson.Options));
    }

    [Theory]
    [InlineData(PreviewMepDevicePlacementItemStatus.Ok, "ok")]
    [InlineData(PreviewMepDevicePlacementItemStatus.OutOfHostBounds, "out_of_host_bounds")]
    [InlineData(PreviewMepDevicePlacementItemStatus.Occupied, "occupied")]
    [InlineData(PreviewMepDevicePlacementItemStatus.UnsupportedPosition, "unsupported_position")]
    public void Placement_item_statuses_serialize_as_closed_snake_case(PreviewMepDevicePlacementItemStatus status, string expected)
    {
        Assert.Equal("\"" + expected + "\"", JsonSerializer.Serialize(status, ContractJson.Options));
    }

    [Theory]
    [InlineData(ApplyMepDevicePlacementsStatus.Applied, "applied")]
    [InlineData(ApplyMepDevicePlacementsStatus.ApprovalRequired, "approval_required")]
    [InlineData(ApplyMepDevicePlacementsStatus.Unavailable, "unavailable")]
    [InlineData(ApplyMepDevicePlacementsStatus.InProgress, "in_progress")]
    [InlineData(ApplyMepDevicePlacementsStatus.Stale, "stale")]
    [InlineData(ApplyMepDevicePlacementsStatus.TransactionFailed, "transaction_failed")]
    [InlineData(ApplyMepDevicePlacementsStatus.CommittedUnverified, "committed_unverified")]
    [InlineData(ApplyMepDevicePlacementsStatus.Indeterminate, "indeterminate")]
    [InlineData(ApplyMepDevicePlacementsStatus.AuditFailed, "audit_failed")]
    public void Apply_statuses_serialize_as_closed_snake_case(ApplyMepDevicePlacementsStatus status, string expected)
    {
        Assert.Equal("\"" + expected + "\"", JsonSerializer.Serialize(status, ContractJson.Options));
    }

    [Fact]
    public void Not_ready_preview_omits_intent_fields()
    {
        var json = JsonSerializer.Serialize(new PreviewMepDevicePlacementsResult
        {
            Context = new PreviewMepDevicePlacementsContext { InstanceId = "instance-a", DocumentId = "doc-a" },
            Ready = false,
            Template = new PreviewMepDeviceTemplate { ElementRef = "template-a", Status = PreviewMepDeviceTemplateStatus.UnsupportedHost },
            Items = [new PreviewMepDevicePlacementItem { OffsetAlongHost = 1, VerticalOffset = 0, Status = PreviewMepDevicePlacementItemStatus.UnsupportedPosition }]
        }, ContractJson.Options);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("ready").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("intent_ref", out _));
        Assert.False(document.RootElement.TryGetProperty("intent_fingerprint", out _));
        Assert.False(document.RootElement.TryGetProperty("expires_at", out _));
    }

    [Fact]
    public void Ready_preview_keeps_intent_fields_and_request_order()
    {
        var json = JsonSerializer.Serialize(new PreviewMepDevicePlacementsResult
        {
            Context = new PreviewMepDevicePlacementsContext { InstanceId = "instance-a", DocumentId = "doc-a" },
            Ready = true,
            Template = new PreviewMepDeviceTemplate
            {
                ElementRef = "template-a",
                Status = PreviewMepDeviceTemplateStatus.Ok,
                CategoryName = "Electrical Fixtures",
                FamilyName = "Duplex Receptacle",
                TypeName = "Standard",
                LevelName = "L4",
                HostElementRef = "wall-a",
                HostName = "Basic Wall"
            },
            Items =
            [
                new PreviewMepDevicePlacementItem { OffsetAlongHost = 1200, VerticalOffset = 0, Status = PreviewMepDevicePlacementItemStatus.Ok },
                new PreviewMepDevicePlacementItem { OffsetAlongHost = 2400, VerticalOffset = 0, Status = PreviewMepDevicePlacementItemStatus.Ok }
            ],
            IntentRef = "abc",
            IntentFingerprint = "def",
            ExpiresAt = new DateTimeOffset(2026, 10, 16, 16, 0, 0, TimeSpan.Zero)
        }, ContractJson.Options);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(1200, document.RootElement.GetProperty("items")[0].GetProperty("offset_along_host").GetDouble());
        Assert.Equal(2400, document.RootElement.GetProperty("items")[1].GetProperty("offset_along_host").GetDouble());
        Assert.Equal("abc", document.RootElement.GetProperty("intent_ref").GetString());
    }

    [Fact]
    public void Non_applied_result_omits_created_refs()
    {
        var json = JsonSerializer.Serialize(
            new ApplyMepDevicePlacementsResult { Status = ApplyMepDevicePlacementsStatus.ApprovalRequired },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("approval_required", document.RootElement.GetProperty("status").GetString());
        Assert.False(document.RootElement.TryGetProperty("created_count", out _));
        Assert.False(document.RootElement.TryGetProperty("created_element_refs", out _));
    }

    [Fact]
    public void Applied_result_keeps_created_refs_in_order()
    {
        var json = JsonSerializer.Serialize(new ApplyMepDevicePlacementsResult
        {
            Status = ApplyMepDevicePlacementsStatus.Applied,
            CreatedCount = 2,
            CreatedElementRefs = ["opaque-ref-a", "opaque-ref-b"]
        }, ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("opaque-ref-a", document.RootElement.GetProperty("created_element_refs")[0].GetString());
        Assert.Equal("opaque-ref-b", document.RootElement.GetProperty("created_element_refs")[1].GetString());
    }

    [Fact]
    public void Contracts_do_not_reference_Revit_API_assemblies()
    {
        var names = typeof(PreviewMepDevicePlacementsRequest).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name);
        Assert.DoesNotContain(names, name => name is not null && name.Contains("Autodesk", StringComparison.Ordinal));
    }
}
