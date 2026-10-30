namespace BimIt.Revit.Assistant.Tests;

/// <summary>
/// The real BimIt.Revit.Server tools/list, read once through the Assistant's own MCP client. Listing tools does not
/// need Revit. Tests use it so no Server schema is copied into the Assistant tests.
/// </summary>
internal static class ServerToolMetadata
{
    private static readonly Lazy<IReadOnlyList<AssistantMcpToolDescriptor>> Tools = new(Load);

    public static IReadOnlyList<AssistantMcpToolDescriptor> Descriptors => Tools.Value;

    public static AssistantMcpToolDescriptor Get(string name) => Descriptors.Single(tool => tool.Name == name);

    private static IReadOnlyList<AssistantMcpToolDescriptor> Load()
    {
        var server = Path.Combine(AppContext.BaseDirectory, "BimIt.Revit.Server.exe");
        if (!File.Exists(server))
        {
            throw new InvalidOperationException("BimIt.Revit.Server.exe is not in the test output.");
        }

        return Task.Run(async () =>
        {
            await using var session = new McpAssistantToolSession(server);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var readiness = await session.InitializeAsync(timeout.Token);
            if (!readiness.Ready || readiness.Tools is null)
            {
                throw new InvalidOperationException("The Server tool list could not be read: " + readiness.Error);
            }

            return readiness.Tools;
        }).GetAwaiter().GetResult();
    }
}
