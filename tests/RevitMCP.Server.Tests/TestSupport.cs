using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;

namespace RevitMCP.Server.Tests;

internal static class TestSupport
{
    public static RevitInstanceRegistration CreateRegistration(
        string instanceId,
        string pipeName,
        string revitVersion = "2026",
        string revitBuild = "26.5.0.55")
    {
        return new RevitInstanceRegistration
        {
            InstanceId = instanceId,
            ProcessId = 4242,
            ProcessStartTimeUtc = new DateTimeOffset(2026, 9, 8, 20, 42, 15, TimeSpan.Zero),
            WindowsSessionId = 7,
            RevitVersion = revitVersion,
            RevitBuild = revitBuild,
            AddinVersion = "0.1.0",
            BridgeProtocolVersion = BridgeProtocol.GetContextVersion,
            PipeName = pipeName,
            RegistrationCreatedUtc = DateTimeOffset.UtcNow
        };
    }

    public static BridgeHandshakeResult CreateHandshake(
        RevitInstanceRegistration registration,
        int selectedProtocolVersion = BridgeProtocol.GetContextVersion)
    {
        return new BridgeHandshakeResult
        {
            InstanceId = registration.InstanceId,
            ProcessId = registration.ProcessId,
            ProcessStartTimeUtc = registration.ProcessStartTimeUtc,
            WindowsSessionId = registration.WindowsSessionId,
            RevitVersion = registration.RevitVersion,
            RevitBuild = registration.RevitBuild,
            AddinVersion = registration.AddinVersion,
            SupportedProtocolVersions = selectedProtocolVersion switch
            {
                BridgeProtocol.DescribeParametersVersion => BridgeProtocol.SupportedVersions,
                BridgeProtocol.GetElementsVersion => BridgeProtocol.GetElementsVersions,
                BridgeProtocol.QueryElementsVersion => BridgeProtocol.QueryElementsVersions,
                BridgeProtocol.GetContextVersion => BridgeProtocol.GetContextVersions,
                _ => BridgeProtocol.HandshakeOnlyVersions
            },
            SelectedProtocolVersion = selectedProtocolVersion
        };
    }

    public static DiscoveredInstance Ready(
        string instanceId,
        string pipeName,
        int protocolVersion = BridgeProtocol.GetContextVersion,
        string revitVersion = "2026",
        string revitBuild = "26.5.0.55")
    {
        var registration = CreateRegistration(instanceId, pipeName, revitVersion, revitBuild);
        return new DiscoveredInstance
        {
            State = DiscoveryState.Ready,
            Registration = registration,
            Handshake = CreateHandshake(registration, protocolVersion)
        };
    }

    public static DiscoveredInstance WithState(
        DiscoveryState state,
        string instanceId,
        string pipeName,
        int? protocolVersion = null)
    {
        var registration = CreateRegistration(instanceId, pipeName);
        return new DiscoveredInstance
        {
            State = state,
            Registration = registration,
            Handshake = protocolVersion is int version ? CreateHandshake(registration, version) : null
        };
    }

    public static GetContextResult ZeroDocument(string instanceId)
    {
        return new GetContextResult
        {
            Instance = new GetContextInstance
            {
                InstanceId = instanceId,
                RevitVersion = "2026",
                RevitBuild = "26.5.0.55"
            },
            Document = null,
            ActiveView = null,
            Selection = new GetContextSelection { Count = 0 }
        };
    }

    public static QueryElementsRequest CreateQueryRequest(
        QueryScope scope = QueryScope.Document,
        string? documentId = null,
        int limit = 50,
        QueryElementFilters? filters = null)
    {
        return new QueryElementsRequest
        {
            DocumentId = documentId,
            Scope = scope,
            Filters = filters ?? new QueryElementFilters { CategoryNames = ["Mechanical Equipment"] },
            Limit = limit
        };
    }

    public static QueryElementsResult CreateQueryResult(
        string instanceId,
        string documentId,
        int matchedCount,
        bool truncated,
        params string[] elementRefs)
    {
        return new QueryElementsResult
        {
            Context = new QueryElementsContext
            {
                InstanceId = instanceId,
                DocumentId = documentId
            },
            MatchedCount = matchedCount,
            Truncated = truncated,
            ElementRefs = elementRefs
        };
    }

    public static JsonElement JsonValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static GetElementsRequest CreateGetElementsRequest(
        string documentId = "doc-1",
        IReadOnlyList<string>? elementRefs = null,
        GetElementsProjection? projection = null)
    {
        return new GetElementsRequest
        {
            DocumentId = documentId,
            ElementRefs = elementRefs ?? ["ref-1"],
            Projection = projection ?? new GetElementsProjection
            {
                Fields = [GetElementField.Name]
            }
        };
    }

    public static GetElementsResult CreateGetElementsResult(
        string instanceId,
        string documentId,
        params GetElementResult[] elements)
    {
        return new GetElementsResult
        {
            Context = new GetElementsContext
            {
                InstanceId = instanceId,
                DocumentId = documentId
            },
            Elements = elements
        };
    }

    public static GetElementResult CreateOkElement(
        string elementRef,
        ProjectedString? name = null,
        IReadOnlyList<GetElementParameter>? parameters = null,
        bool? parametersTruncated = null)
    {
        return new GetElementResult
        {
            ElementRef = elementRef,
            Status = GetElementResultStatus.Ok,
            Name = name ?? ProjectedString.Omitted,
            Parameters = parameters,
            ParametersTruncated = parametersTruncated
        };
    }

    public static GetElementResult CreateNotFoundElement(string elementRef)
    {
        return new GetElementResult
        {
            ElementRef = elementRef,
            Status = GetElementResultStatus.NotFound
        };
    }
}

internal static class McpToolInvoke
{
    public static async Task<CallToolResult> InvokeAsync(
        McpServerTool tool,
        IDictionary<string, JsonElement>? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        await using var transport = new UnusedTransport();
        await using var server = McpServer.Create(
            transport,
            new McpServerOptions
            {
                ServerInfo = new Implementation
                {
                    Name = "RevitMCP.Server.Tests",
                    Version = "0.1.0"
                }
            });

        var request = new RequestContext<CallToolRequestParams>(
            server,
            new JsonRpcRequest
            {
                Method = "tools/call",
                Id = new RequestId("strict-input")
            },
            new CallToolRequestParams
            {
                Name = tool.ProtocolTool.Name,
                Arguments = arguments
            });

        return await tool.InvokeAsync(request, CancellationToken.None);
    }
}

internal sealed class UnusedTransport : ITransport
{
    private readonly Channel<JsonRpcMessage> _messages = Channel.CreateUnbounded<JsonRpcMessage>();

    public ChannelReader<JsonRpcMessage> MessageReader => _messages.Reader;

    public string? SessionId => null;

    public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        _ = message;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _messages.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeDiscovery : IRevitInstanceDiscovery
{
    public List<DiscoveredInstance> Instances { get; } = [];

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<DiscoveredInstance>> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Task.FromResult<IReadOnlyList<DiscoveredInstance>>(Instances.ToArray());
    }
}

internal sealed class RecordingBridgeClient : IRevitBridgeClient
{
    public bool Disposed { get; private set; }

    public BridgeHandshakeRequest? HandshakeRequest { get; private set; }

    public int GetContextCalls { get; private set; }

    public Func<BridgeHandshakeRequest, CancellationToken, Task<BridgeHandshakeResult>>? Handshake { get; set; }

    public Func<GetContextRequest, TimeSpan, CancellationToken, Task<GetContextResult>>? GetContext { get; set; }

    public int QueryElementsCalls { get; private set; }

    public QueryElementsRequest? LastQueryRequest { get; private set; }

    public Func<QueryElementsRequest, TimeSpan, CancellationToken, Task<QueryElementsResult>>? QueryElements { get; set; }

    public Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
    {
        HandshakeRequest = request;
        return Handshake is null
            ? throw new InvalidOperationException("Handshake handler was not configured.")
            : Handshake(request, cancellationToken);
    }

    public Task<GetContextResult> GetContextAsync(
        GetContextRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        GetContextCalls++;
        return GetContext is null
            ? throw new InvalidOperationException("GetContext handler was not configured.")
            : GetContext(request, timeout, cancellationToken);
    }

    public Task<QueryElementsResult> QueryElementsAsync(
        QueryElementsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        QueryElementsCalls++;
        LastQueryRequest = request;
        return QueryElements is null
            ? throw new NotSupportedException("This recording client does not implement revit.query_elements.")
            : QueryElements(request, timeout, cancellationToken);
    }

    public int GetElementsCalls { get; private set; }

    public GetElementsRequest? LastGetElementsRequest { get; private set; }

    public Func<GetElementsRequest, TimeSpan, CancellationToken, Task<GetElementsResult>>? GetElements { get; set; }

    public Task<GetElementsResult> GetElementsAsync(
        GetElementsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        GetElementsCalls++;
        LastGetElementsRequest = request;
        return GetElements is null
            ? throw new NotSupportedException("This recording client does not implement revit.get_elements.")
            : GetElements(request, timeout, cancellationToken);
    }

    public int DescribeParametersCalls { get; private set; }

    public DescribeParametersRequest? LastDescribeParametersRequest { get; private set; }

    public Func<DescribeParametersRequest, TimeSpan, CancellationToken, Task<DescribeParametersResult>>? DescribeParameters { get; set; }

    public Task<DescribeParametersResult> DescribeParametersAsync(
        DescribeParametersRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DescribeParametersCalls++;
        LastDescribeParametersRequest = request;
        return DescribeParameters is null
            ? throw new NotSupportedException("This recording client does not implement revit.describe_parameters.")
            : DescribeParameters(request, timeout, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingBridgeClientFactory : IBridgeClientFactory
{
    public List<string> RequestedPipes { get; } = [];

    public List<RecordingBridgeClient> Clients { get; } = [];

    public Func<string, TimeSpan, CancellationToken, Task<RecordingBridgeClient>>? Connect { get; set; }

    public async Task<IRevitBridgeClient> ConnectAsync(
        string pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        RequestedPipes.Add(pipeName);
        if (Connect is null)
        {
            throw new InvalidOperationException("Connect handler was not configured.");
        }

        var client = await Connect(pipeName, timeout, cancellationToken).ConfigureAwait(false);
        Clients.Add(client);
        return client;
    }
}
