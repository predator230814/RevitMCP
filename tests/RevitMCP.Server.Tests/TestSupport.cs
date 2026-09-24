using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

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
                BridgeProtocol.GetMepTopologyVersion => BridgeProtocol.SupportedVersions,
                BridgeProtocol.GetParameterValuesVersion => BridgeProtocol.GetParameterValuesVersions,
                BridgeProtocol.DescribeParametersVersion => BridgeProtocol.DescribeParametersVersions,
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

    public static DescribeParametersRequest CreateDescribeParametersRequest(
        string documentId = "doc-1",
        IReadOnlyList<string>? elementRefs = null,
        DescribeParameterSource source = DescribeParameterSource.Both,
        string? nameContains = null,
        int limit = 50)
    {
        return new DescribeParametersRequest
        {
            DocumentId = documentId,
            ElementRefs = elementRefs ?? ["ref-1"],
            Source = source,
            NameContains = nameContains,
            Limit = limit
        };
    }

    public static DescribeParametersResult CreateDescribeParametersResult(
        string instanceId,
        string documentId,
        IReadOnlyList<DescribeParameterElementResult> elements,
        int matchedCount,
        bool truncated,
        params DescribeParameterDescriptor[] parameters)
    {
        return new DescribeParametersResult
        {
            Context = new DescribeParametersContext
            {
                InstanceId = instanceId,
                DocumentId = documentId
            },
            Elements = elements,
            MatchedCount = matchedCount,
            Truncated = truncated,
            Parameters = parameters
        };
    }

    public static DescribeParameterElementResult CreateDescribeOkElement(string elementRef)
    {
        return new DescribeParameterElementResult
        {
            ElementRef = elementRef,
            Status = GetElementResultStatus.Ok
        };
    }

    public static DescribeParameterElementResult CreateDescribeNotFoundElement(string elementRef)
    {
        return new DescribeParameterElementResult
        {
            ElementRef = elementRef,
            Status = GetElementResultStatus.NotFound
        };
    }

    public static DescribeParameterDescriptor CreateBuiltInDescriptor(
        string parameterRef = "pref-1",
        string name = "Flow",
        GetElementParameterSource source = GetElementParameterSource.Instance,
        string parameterTypeId = "autodesk.revit.parameter:rbsPipeFlowParam",
        string forgeTypeId = "autodesk.spec.aec.piping:flow",
        DescribeParameterDataTypeKind dataTypeKind = DescribeParameterDataTypeKind.MeasurableSpec,
        int presentOnCount = 1,
        int readOnlyOnCount = 0)
    {
        return new DescribeParameterDescriptor
        {
            ParameterRef = parameterRef,
            Name = name,
            Source = source,
            Identity = new DescribeParameterIdentity
            {
                Kind = DescribeParameterIdentityKind.BuiltIn,
                ParameterTypeId = parameterTypeId
            },
            DataType = new DescribeParameterDataType
            {
                Kind = dataTypeKind,
                ForgeTypeId = forgeTypeId
            },
            PresentOnCount = presentOnCount,
            ReadOnlyOnCount = readOnlyOnCount
        };
    }

    public static GetParameterValuesRequest CreateGetParameterValuesRequest(
        string documentId = "doc-1",
        IReadOnlyList<GetParameterValueRead>? reads = null)
    {
        return new GetParameterValuesRequest
        {
            DocumentId = documentId,
            Reads = reads ??
            [
                new GetParameterValueRead
                {
                    ElementRef = "ref-1",
                    ParameterRef = "pref-1"
                }
            ]
        };
    }

    public static GetParameterValuesResult CreateGetParameterValuesResult(
        string instanceId,
        string documentId,
        params GetParameterValueItem[] items)
    {
        return new GetParameterValuesResult
        {
            Context = new DescribeParametersContext
            {
                InstanceId = instanceId,
                DocumentId = documentId
            },
            Items = items
        };
    }

    public static GetParameterValueItem CreateParameterValueFailure(
        string elementRef,
        string parameterRef,
        GetParameterValueStatus status)
    {
        return new GetParameterValueItem
        {
            ElementRef = elementRef,
            ParameterRef = parameterRef,
            Status = status
        };
    }

    public static GetParameterValueItem CreateParameterValueOk(
        string elementRef,
        string parameterRef,
        DescribeParameterDataType? dataType = null,
        bool hasValue = false,
        GetParameterValue? value = null)
    {
        return new GetParameterValueItem
        {
            ElementRef = elementRef,
            ParameterRef = parameterRef,
            Status = GetParameterValueStatus.Ok,
            DataType = dataType ?? new DescribeParameterDataType
            {
                Kind = DescribeParameterDataTypeKind.Unknown
            },
            HasValue = hasValue,
            Value = value
        };
    }

    public static GetMepTopologyRequest CreateGetMepTopologyRequest(
        string documentId = "doc-1",
        IReadOnlyList<string>? seedElementRefs = null,
        MepTopologyDomain? domain = null,
        int? maxDepth = null,
        int? maxElements = null,
        int? maxEdges = null)
    {
        return new GetMepTopologyRequest
        {
            DocumentId = documentId,
            SeedElementRefs = seedElementRefs ?? ["ref-1"],
            Domain = domain,
            MaxDepth = maxDepth,
            MaxElements = maxElements,
            MaxEdges = maxEdges
        };
    }

    public static GetMepTopologyResult CreateGetMepTopologyResult(
        string instanceId,
        string documentId,
        IReadOnlyList<GetMepTopologySeed>? seeds = null,
        IReadOnlyList<GetMepTopologyNode>? nodes = null,
        IReadOnlyList<GetMepTopologyEdge>? edges = null,
        bool truncated = false,
        IReadOnlyList<MepTopologyTruncationReason>? truncationReasons = null)
    {
        return new GetMepTopologyResult
        {
            Context = new GetMepTopologyContext
            {
                InstanceId = instanceId,
                DocumentId = documentId
            },
            Seeds = seeds ??
            [
                new GetMepTopologySeed
                {
                    ElementRef = "ref-1",
                    Status = MepTopologySeedStatus.Ok
                }
            ],
            Nodes = nodes ??
            [
                new GetMepTopologyNode
                {
                    ElementRef = "ref-1",
                    Depth = 0
                }
            ],
            Edges = edges ?? [],
            Truncated = truncated,
            TruncationReasons = truncationReasons ?? []
        };
    }

    public static void AssertOptionalNullableInstanceId(JsonElement property)
    {
        Assert.Equal(
            new[] { "string", "null" },
            property.GetProperty("type").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.False(property.TryGetProperty("format", out var format) && format.GetString() is "uuid" or "guid");
        Assert.False(property.TryGetProperty("minLength", out _));
    }

    public static void AssertRequiredOpaqueString(JsonElement property)
    {
        Assert.Equal("string", property.GetProperty("type").GetString());
        Assert.False(property.TryGetProperty("format", out var format) && format.GetString() is "uuid" or "guid");
        Assert.False(property.TryGetProperty("minLength", out _));
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

    public int GetParameterValuesCalls { get; private set; }

    public GetParameterValuesRequest? LastGetParameterValuesRequest { get; private set; }

    public Func<GetParameterValuesRequest, TimeSpan, CancellationToken, Task<GetParameterValuesResult>>? GetParameterValues { get; set; }

    public Task<GetParameterValuesResult> GetParameterValuesAsync(
        GetParameterValuesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        GetParameterValuesCalls++;
        LastGetParameterValuesRequest = request;
        return GetParameterValues is null
            ? throw new NotSupportedException("This recording client does not implement revit.get_parameter_values.")
            : GetParameterValues(request, timeout, cancellationToken);
    }

    public int GetMepTopologyCalls { get; private set; }

    public GetMepTopologyRequest? LastGetMepTopologyRequest { get; private set; }

    public TimeSpan? LastGetMepTopologyTimeout { get; private set; }

    public Func<GetMepTopologyRequest, TimeSpan, CancellationToken, Task<GetMepTopologyResult>>? GetMepTopology { get; set; }

    public Task<GetMepTopologyResult> GetMepTopologyAsync(
        GetMepTopologyRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        GetMepTopologyCalls++;
        LastGetMepTopologyRequest = request;
        LastGetMepTopologyTimeout = timeout;
        return GetMepTopology is null
            ? throw new NotSupportedException("This recording client does not implement revit.get_mep_topology.")
            : GetMepTopology(request, timeout, cancellationToken);
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
