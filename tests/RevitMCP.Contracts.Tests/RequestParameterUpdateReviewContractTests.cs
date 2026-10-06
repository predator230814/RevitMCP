using System.Text.Json;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class RequestParameterUpdateReviewContractTests
{
    [Fact]
    public void Request_serializes_intent_ref_as_snake_case()
    {
        var json = JsonSerializer.Serialize(
            new RequestParameterUpdateReviewRequest { IntentRef = "abc123" },
            ContractJson.Options);

        Assert.Contains("\"intent_ref\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("IntentRef", json, StringComparison.Ordinal);
        Assert.DoesNotContain("instance_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("document_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("session_ref", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RequestParameterUpdateReviewStatus.Started, "started")]
    [InlineData(RequestParameterUpdateReviewStatus.AlreadyActive, "already_active")]
    [InlineData(RequestParameterUpdateReviewStatus.Busy, "busy")]
    [InlineData(RequestParameterUpdateReviewStatus.Unavailable, "unavailable")]
    [InlineData(RequestParameterUpdateReviewStatus.Terminal, "terminal")]
    public void Status_serializes_as_snake_case(RequestParameterUpdateReviewStatus status, string expected)
    {
        var json = JsonSerializer.Serialize(status, ContractJson.Options);
        Assert.Equal("\"" + expected + "\"", json);
    }

    [Fact]
    public void Result_contains_only_status()
    {
        Assert.Equal(
            [nameof(RequestParameterUpdateReviewResult.Status)],
            typeof(RequestParameterUpdateReviewResult).GetProperties().Select(property => property.Name).ToArray());
        Assert.Equal(
            [nameof(RequestParameterUpdateReviewRequest.IntentRef)],
            typeof(RequestParameterUpdateReviewRequest).GetProperties().Select(property => property.Name).ToArray());

        var json = JsonSerializer.Serialize(
            new RequestParameterUpdateReviewResult { Status = RequestParameterUpdateReviewStatus.Started },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("started", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("status", Assert.Single(document.RootElement.EnumerateObject()).Name);
        Assert.DoesNotContain("session_ref", json, StringComparison.Ordinal);
        Assert.DoesNotContain("intent_ref", json, StringComparison.Ordinal);
        Assert.DoesNotContain("intent_fingerprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("instance_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("document_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("approved", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rejected", json, StringComparison.Ordinal);
    }
}
