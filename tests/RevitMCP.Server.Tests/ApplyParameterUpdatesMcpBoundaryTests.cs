using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class ApplyParameterUpdatesMcpBoundaryTests
{
    [Fact]
    public void Tool_annotations_and_closed_schemas_match_cap_0008()
    {
        var tool = CreateProtocolTool();
        Assert.Equal("revit_apply_parameter_updates", tool.Name);
        Assert.Equal("Apply Revit Parameter Updates", tool.Title);
        Assert.Contains("previously previewed", tool.Description, StringComparison.Ordinal);
        Assert.Contains("locally approved", tool.Description, StringComparison.Ordinal);
        Assert.False(tool.Annotations!.ReadOnlyHint);
        Assert.True(tool.Annotations.DestructiveHint);
        Assert.False(tool.Annotations.IdempotentHint);
        Assert.False(tool.Annotations.OpenWorldHint);

        var input = tool.InputSchema;
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "instance_id", "intent_ref" },
            input.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "instance_id", "intent_ref" },
            input.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(input.GetProperty("properties").TryGetProperty("document_id", out _));
        Assert.False(input.GetProperty("properties").TryGetProperty("values", out _));
        Assert.False(input.GetProperty("properties").TryGetProperty("confirm", out _));
        Assert.False(input.GetProperty("properties").TryGetProperty("approval", out _));

        var output = tool.OutputSchema!.Value;
        Assert.False(output.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "status" }, output.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[]
            {
                "applied",
                "approval_required",
                "unavailable",
                "in_progress",
                "stale",
                "transaction_failed",
                "committed_unverified",
                "indeterminate",
                "audit_failed"
            },
            output.GetProperty("properties").GetProperty("status").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    [Fact]
    public async Task Unexpected_properties_are_rejected_before_discovery()
    {
        foreach (var name in new[] { "document_id", "fingerprint", "session_ref", "values", "confirm", "approval" })
        {
            var (tool, discovery, factory) = CreateInvocableTool();
            var arguments = ValidArguments();
            arguments[name] = TestSupport.JsonValue("\"extra\"");
            var result = await McpToolInvoke.InvokeAsync(tool, arguments);
            Assert.True(result.IsError);
            Assert.Equal(0, discovery.CallCount);
            Assert.Empty(factory.RequestedPipes);
        }
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
    public async Task Structured_output_is_only_status(ApplyParameterUpdatesStatus status, string wire)
    {
        var (tool, _, factory) = CreateInvocableTool(status);
        var result = await McpToolInvoke.InvokeAsync(tool, ValidArguments());
        Assert.False(result.IsError);
        var structured = result.StructuredContent!.Value;
        Assert.Equal(wire, structured.GetProperty("status").GetString());
        Assert.Equal("status", Assert.Single(structured.EnumerateObject()).Name);
        Assert.Equal("intent-ref", factory.Clients[0].LastApplyParameterUpdatesRequest!.IntentRef);
    }

    [Fact]
    public async Task Exact_instance_is_required_and_v9_does_not_fall_back()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v9", "pipe-v9", protocolVersion: 9));
        discovery.Instances.Add(TestSupport.Ready("v10", "pipe-v10", protocolVersion: 10));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new InvalidOperationException("v9 must not be contacted.")
        };
        var service = new ApplyParameterUpdatesApplicationService(discovery, factory, new ServerTimeouts());
        var unavailable = await service.ExecuteAsync("v9", new ApplyParameterUpdatesRequest { IntentRef = "intent-ref" }, CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, unavailable.ErrorCode);
        Assert.Empty(factory.RequestedPipes);

        var missing = await service.ExecuteAsync("missing", new ApplyParameterUpdatesRequest { IntentRef = "intent-ref" }, CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceNotFound, missing.ErrorCode);

        var omitted = await service.ExecuteAsync(null!, new ApplyParameterUpdatesRequest { IntentRef = "intent-ref" }, CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceNotFound, omitted.ErrorCode);
        Assert.False(InstanceTargetResolver.IsApplyParameterUpdatesEligible(TestSupport.Ready("v11", "pipe-v11", protocolVersion: 11)));
        Assert.True(InstanceTargetResolver.IsApplyParameterUpdatesEligible(TestSupport.Ready("v10", "pipe-v10", protocolVersion: 10)));
    }

    private static Tool CreateProtocolTool() => CreateInvocableTool().Tool.ProtocolTool;

    private static (McpServerTool Tool, FakeDiscovery Discovery, RecordingBridgeClientFactory Factory) CreateInvocableTool(
        ApplyParameterUpdatesStatus status = ApplyParameterUpdatesStatus.Applied)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 10));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) =>
            {
                var registration = TestSupport.CreateRegistration("only", pipe);
                return Task.FromResult(new RecordingBridgeClient
                {
                    Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, 10)),
                    ApplyParameterUpdates = (_, _, _) => Task.FromResult(new ApplyParameterUpdatesResult { Status = status })
                });
            }
        };
        var application = new ApplyParameterUpdatesApplicationService(discovery, factory, new ServerTimeouts());
        var tool = ApplyParameterUpdatesToolRegistration.Create(new ApplyParameterUpdatesMcpTools(application));
        return (tool, discovery, factory);
    }

    private static Dictionary<string, JsonElement> ValidArguments()
        => new(StringComparer.Ordinal)
        {
            ["instance_id"] = TestSupport.JsonValue("\"only\""),
            ["intent_ref"] = TestSupport.JsonValue("\"intent-ref\"")
        };
}
