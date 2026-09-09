using System.Text.Json;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class RegistrationSerializationTests
{
    [Fact]
    public void Registration_uses_snake_case_json_field_names()
    {
        var registration = new RevitInstanceRegistration
        {
            InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
            ProcessId = 18432,
            ProcessStartTimeUtc = new DateTimeOffset(2026, 9, 8, 20, 42, 15, TimeSpan.Zero),
            WindowsSessionId = 2,
            RevitVersion = "2026",
            RevitBuild = "26.5.0.0",
            AddinVersion = "0.1.0",
            BridgeProtocolVersion = 1,
            PipeName = "revitmcp.bridge.v1.s2.a81e3fd06d574d278dbcb2f9e98221ac",
            RegistrationCreatedUtc = new DateTimeOffset(2026, 9, 8, 20, 42, 16, TimeSpan.Zero)
        };

        var json = JsonSerializer.Serialize(registration, ContractJson.Options);

        Assert.Contains("\"instance_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"process_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"process_start_time_utc\"", json, StringComparison.Ordinal);
        Assert.Contains("\"windows_session_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"revit_version\"", json, StringComparison.Ordinal);
        Assert.Contains("\"revit_build\"", json, StringComparison.Ordinal);
        Assert.Contains("\"addin_version\"", json, StringComparison.Ordinal);
        Assert.Contains("\"bridge_protocol_version\"", json, StringComparison.Ordinal);
        Assert.Contains("\"pipe_name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"registration_created_utc\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("InstanceId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_round_trips_opaque_instance_id()
    {
        var original = new RevitInstanceRegistration
        {
            InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
            ProcessId = 11,
            ProcessStartTimeUtc = DateTimeOffset.Parse("2026-09-08T20:42:15Z"),
            WindowsSessionId = 1,
            RevitVersion = "2027",
            RevitBuild = "27.0.0.0",
            AddinVersion = "0.1.0",
            BridgeProtocolVersion = 1,
            PipeName = "revitmcp.bridge.v1.s1.a81e3fd06d574d278dbcb2f9e98221ac",
            RegistrationCreatedUtc = DateTimeOffset.Parse("2026-09-08T20:42:16Z")
        };

        var json = JsonSerializer.Serialize(original, ContractJson.Options);
        var restored = JsonSerializer.Deserialize<RevitInstanceRegistration>(json, ContractJson.Options);

        Assert.NotNull(restored);
        Assert.Equal(original.InstanceId, restored.InstanceId);
        Assert.Equal(original.ProcessId, restored.ProcessId);
        Assert.Equal(original.PipeName, restored.PipeName);
    }
}
