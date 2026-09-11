using System.Diagnostics;
using RevitMCP.Addin.Lifecycle;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class GetElementsCompositionTests
{
    [Fact]
    public void Production_lifetime_creates_get_elements_with_the_shared_identity()
    {
        var adapters = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Lifecycle", "RevitLifecycleAdapters.cs"));
        Assert.Contains("new RevitQueryElementsService(_dispatcher, metadata, _identity)", adapters, StringComparison.Ordinal);
        Assert.Contains("new RevitGetElementsService(_dispatcher, metadata, _identity)", adapters, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(adapters, "new OpenDocumentIdentityService()"));
        Assert.Contains("private readonly OpenDocumentIdentityService _identity;", adapters, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Incomplete_fake_compositions_advertise_only_the_valid_protocol_prefix()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var full = await StartAsync(new RecordingCapabilityService(), new RecordingQueryElementsService(), new RecordingGetElementsService());
        await using var query = await StartAsync(new RecordingCapabilityService(), new RecordingQueryElementsService(), getElements: null);
        await using var context = await StartAsync(new RecordingCapabilityService(), query: null, getElements: null);
        await using var handshake = await StartAsync(capability: null, query: null, getElements: null);
        await using var getOnly = await StartAsync(capability: null, query: null, getElements: new RecordingGetElementsService());
        await using var contextGet = await StartAsync(new RecordingCapabilityService(), query: null, getElements: new RecordingGetElementsService());
        await using var queryGet = await StartAsync(capability: null, query: new RecordingQueryElementsService(), getElements: new RecordingGetElementsService());

        Assert.Equal(BridgeProtocol.SupportedVersions, full.Metadata.SupportedProtocolVersions);
        Assert.Equal(4, full.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.QueryElementsVersions, query.Metadata.SupportedProtocolVersions);
        Assert.Equal(3, query.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.GetContextVersions, context.Metadata.SupportedProtocolVersions);
        Assert.Equal(2, context.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, handshake.Metadata.SupportedProtocolVersions);
        Assert.Equal(1, handshake.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, getOnly.Metadata.SupportedProtocolVersions);
        Assert.Equal(1, getOnly.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.GetContextVersions, contextGet.Metadata.SupportedProtocolVersions);
        Assert.Equal(2, contextGet.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, queryGet.Metadata.SupportedProtocolVersions);
        Assert.Equal(1, queryGet.Registration?.BridgeProtocolVersion);
    }

    private static async Task<NamedPipeBridgeHost> StartAsync(
        IRevitCapabilityService? capability,
        IRevitQueryElementsService? query,
        IRevitGetElementsService? getElements)
    {
        var process = Process.GetCurrentProcess();
        var metadata = new BridgeInstanceMetadata
        {
            InstanceId = Guid.NewGuid().ToString("D"),
            ProcessId = process.Id,
            ProcessStartTimeUtc = new DateTimeOffset(process.StartTime).ToUniversalTime(),
            WindowsSessionId = process.SessionId,
            RevitVersion = "2026",
            RevitBuild = "26.5.0.0",
            AddinVersion = "1.0.0",
            SupportedProtocolVersions = BridgeProtocol.SupportedVersions
        };
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        return await NamedPipeBridgeHost.StartAsync(
            metadata,
            new FileRegistrationStore(root),
            capability,
            query,
            getElements,
            CancellationToken.None);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
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

        throw new InvalidOperationException("Could not locate RevitMCP.sln from the test output directory.");
    }
}
