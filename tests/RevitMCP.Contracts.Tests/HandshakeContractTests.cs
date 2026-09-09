using System.Text.Json;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class HandshakeContractTests
{
    private static readonly string[] ForbiddenFragments =
    [
        "document",
        "view",
        "selection",
        "username",
        "user_name",
        "file_path",
        "path",
        "cloud",
        "project"
    ];

    [Fact]
    public void Handshake_request_and_result_contain_no_model_user_or_project_metadata()
    {
        var request = new BridgeHandshakeRequest
        {
            ExpectedInstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
            SupportedProtocolVersions = [1],
            ClientName = "RevitMCP.Server",
            ClientVersion = "0.1.0"
        };
        var result = new BridgeHandshakeResult
        {
            InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
            ProcessId = 18432,
            ProcessStartTimeUtc = DateTimeOffset.Parse("2026-09-08T20:42:15Z"),
            WindowsSessionId = 2,
            RevitVersion = "2026",
            RevitBuild = "26.5.0.0",
            AddinVersion = "0.1.0",
            SupportedProtocolVersions = [1],
            SelectedProtocolVersion = 1
        };

        var json = JsonSerializer.Serialize(request, ContractJson.Options)
            + JsonSerializer.Serialize(result, ContractJson.Options);

        foreach (var fragment in ForbiddenFragments)
        {
            Assert.DoesNotContain(fragment, json, StringComparison.OrdinalIgnoreCase);
        }

        var propertyNames = typeof(BridgeHandshakeRequest).GetProperties().Select(property => property.Name)
            .Concat(typeof(BridgeHandshakeResult).GetProperties().Select(property => property.Name))
            .ToArray();

        foreach (var fragment in ForbiddenFragments)
        {
            Assert.DoesNotContain(propertyNames, name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }
}
