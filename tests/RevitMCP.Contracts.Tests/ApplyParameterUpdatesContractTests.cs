using System.Text.Json;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class ApplyParameterUpdatesContractTests
{
    [Fact]
    public void Request_contains_only_intent_ref()
    {
        Assert.Equal(
            [nameof(ApplyParameterUpdatesRequest.IntentRef)],
            typeof(ApplyParameterUpdatesRequest).GetProperties().Select(property => property.Name).ToArray());

        var json = JsonSerializer.Serialize(
            new ApplyParameterUpdatesRequest { IntentRef = "abc123" },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("intent_ref", Assert.Single(document.RootElement.EnumerateObject()).Name);
        Assert.Equal("abc123", document.RootElement.GetProperty("intent_ref").GetString());
        AssertForbidden(json);
    }

    [Fact]
    public void Result_contains_only_status()
    {
        Assert.Equal(
            [nameof(ApplyParameterUpdatesResult.Status)],
            typeof(ApplyParameterUpdatesResult).GetProperties().Select(property => property.Name).ToArray());

        var json = JsonSerializer.Serialize(
            new ApplyParameterUpdatesResult { Status = ApplyParameterUpdatesStatus.Applied },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("status", Assert.Single(document.RootElement.EnumerateObject()).Name);
        Assert.Equal("applied", document.RootElement.GetProperty("status").GetString());
        AssertForbidden(json);
    }

    [Theory]
    [InlineData(ApplyParameterUpdatesStatus.Applied, "applied")]
    [InlineData(ApplyParameterUpdatesStatus.ApprovalRequired, "approval_required")]
    [InlineData(ApplyParameterUpdatesStatus.Unavailable, "unavailable")]
    [InlineData(ApplyParameterUpdatesStatus.InProgress, "in_progress")]
    [InlineData(ApplyParameterUpdatesStatus.Stale, "stale")]
    [InlineData(ApplyParameterUpdatesStatus.TransactionFailed, "transaction_failed")]
    [InlineData(ApplyParameterUpdatesStatus.CommittedUnverified, "committed_unverified")]
    [InlineData(ApplyParameterUpdatesStatus.Indeterminate, "indeterminate")]
    [InlineData(ApplyParameterUpdatesStatus.AuditFailed, "audit_failed")]
    public void Status_wire_values_are_exact(ApplyParameterUpdatesStatus status, string expected)
    {
        var json = JsonSerializer.Serialize(status, ContractJson.Options);
        Assert.Equal("\"" + expected + "\"", json);
    }

    [Fact]
    public void Apply_support_is_protocol_v10_only()
    {
        for (var version = 1; version <= 9; version++)
        {
            Assert.False(BridgeProtocol.SupportsApplyParameterUpdates(version));
        }

        Assert.True(BridgeProtocol.SupportsApplyParameterUpdates(10));
        Assert.False(BridgeProtocol.SupportsApplyParameterUpdates(11));
        Assert.Equal(10, BridgeProtocol.CurrentVersion);
    }

    private static void AssertForbidden(string json)
    {
        foreach (var field in new[]
        {
            "fingerprint",
            "session",
            "value",
            "document",
            "approval",
            "audit",
            "instance_id",
            "confirm"
        })
        {
            Assert.DoesNotContain(field, json, StringComparison.Ordinal);
        }
    }
}
