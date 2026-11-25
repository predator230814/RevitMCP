using System.Text.Json;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using BimIt.Revit.Server;
using ModelContextProtocol.Protocol;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class RunCSharpQueryMcpBoundaryTests
{
    private const string V26 = "id-v26";
    private const string P26 = "pipe-v26";
    private const string V25 = "id-v25";
    private const string P25 = "pipe-v25";
    private const string Source = "return 1;";
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Cache = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Tool_identity_hints_and_description_match_the_contract()
    {
        var tool = Tool().ProtocolTool;
        Assert.Equal("revit_run_csharp_query", tool.Name);
        Assert.Equal("Run bounded Revit C# query", tool.Title);
        Assert.True(tool.Annotations!.ReadOnlyHint);
        Assert.False(tool.Annotations.OpenWorldHint);
        Assert.NotNull(tool.OutputSchema);
        Assert.DoesNotContain("revit.run_csharp_query", tool.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("Roslyn", tool.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("CompilerHost", tool.Description, StringComparison.Ordinal);
        Assert.DoesNotContain('\\', tool.Description!);
        AssertOrder(
            tool.Description!,
            [
                "bounded read-only C# method body",
                "active Revit project",
                "existing MCP primitives",
                "revit_lookup and revit_read",
                "revit_api.symbols",
                "document_id comes from revit_get_context",
                "local Revit opt-in must be enabled",
                "doc, elementRef, and the fixed usings",
                "not a general-purpose script runner",
                "does not authorize writes"
            ]);
    }

    [Fact]
    public void Input_schema_is_closed_and_output_leaves_result_unconstrained()
    {
        var tool = Tool().ProtocolTool;
        var input = tool.InputSchema;
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["document_id", "source"], Strings(input.GetProperty("required")));
        Assert.Equal(["document_id", "instance_id", "source"], Names(input.GetProperty("properties")));
        Assert.Equal(1, input.GetProperty("properties").GetProperty("document_id").GetProperty("minLength").GetInt32());
        Assert.Equal(1, input.GetProperty("properties").GetProperty("source").GetProperty("minLength").GetInt32());
        Assert.Equal(8000, input.GetProperty("properties").GetProperty("source").GetProperty("maxLength").GetInt32());
        Assert.False(input.GetProperty("properties").TryGetProperty("references", out _));
        Assert.False(input.GetProperty("properties").TryGetProperty("usings", out _));
        Assert.False(input.GetProperty("properties").TryGetProperty("options", out _));

        var output = tool.OutputSchema!.Value;
        Assert.False(output.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["result", "source_sha256", "cache_key", "policy_version", "revit_api", "compile_outcome", "duration_ms", "result_characters"],
            Strings(output.GetProperty("required")));
        var result = output.GetProperty("properties").GetProperty("result");
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.False(result.EnumerateObject().Any());
        Assert.Equal("^[0-9a-f]{64}$", output.GetProperty("properties").GetProperty("source_sha256").GetProperty("pattern").GetString());
        Assert.Equal("^[0-9a-f]{64}$", output.GetProperty("properties").GetProperty("cache_key").GetProperty("pattern").GetString());
        Assert.Equal(1, output.GetProperty("properties").GetProperty("policy_version").GetProperty("const").GetInt32());
        Assert.Equal("success", output.GetProperty("properties").GetProperty("compile_outcome").GetProperty("const").GetString());
        Assert.Equal(0, output.GetProperty("properties").GetProperty("duration_ms").GetProperty("minimum").GetInt32());
        Assert.Equal(0, output.GetProperty("properties").GetProperty("result_characters").GetProperty("minimum").GetInt32());
        Assert.Equal(16000, output.GetProperty("properties").GetProperty("result_characters").GetProperty("maximum").GetInt32());
        var api = output.GetProperty("properties").GetProperty("revit_api");
        Assert.False(api.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("RevitAPI", api.GetProperty("properties").GetProperty("name").GetProperty("const").GetString());
        Assert.Equal("26.5.0.0", api.GetProperty("properties").GetProperty("version").GetProperty("const").GetString());
    }

    [Theory]
    [InlineData("references", "[]")]
    [InlineData("usings", "[]")]
    [InlineData("options", "{}")]
    [InlineData("compiler_path", "\"C:\\\\secret\"")]
    public async Task Unknown_input_field_is_invalid_before_routing(string property, string json)
    {
        var client = Client(V26, P26, 26);
        var arguments = Arguments();
        arguments[property] = Json(json);
        var result = await Invoke(client, arguments);
        AssertInvalidRequest(result);
        Assert.Equal(0, client.RunCSharpQueryCalls);
    }

    [Fact]
    public async Task Missing_document_id_source_and_oversized_source_are_invalid_before_routing()
    {
        var missingDocument = Client(V26, P26, 26);
        var withoutDocument = Arguments();
        withoutDocument.Remove("document_id");
        AssertInvalidRequest(await Invoke(missingDocument, withoutDocument));
        Assert.Equal(0, missingDocument.RunCSharpQueryCalls);

        var missingSource = Client(V26, P26, 26);
        var withoutSource = Arguments();
        withoutSource.Remove("source");
        AssertInvalidRequest(await Invoke(missingSource, withoutSource));
        Assert.Equal(0, missingSource.RunCSharpQueryCalls);

        var oversized = Client(V26, P26, 26);
        var tooLong = Arguments();
        tooLong["source"] = Json("\"" + new string('a', 8001) + "\"");
        AssertInvalidRequest(await Invoke(oversized, tooLong));
        Assert.Equal(0, oversized.RunCSharpQueryCalls);
    }

    [Fact]
    public async Task One_protocol_26_host_is_selected_and_a_protocol_25_host_is_not()
    {
        var v26 = Client(V26, P26, 26);
        var v25 = Client(V25, P25, 25);
        var factory = Factory(new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal)
        {
            [P26] = v26,
            [P25] = v25
        });
        var outcome = await Service(factory, (V26, P26, 26), (V25, P25, 25)).ExecuteAsync(null, Request(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal([P26], factory.RequestedPipes);
        Assert.Equal(1, v26.RunCSharpQueryCalls);
        Assert.Equal(0, v25.RunCSharpQueryCalls);
        Assert.Equal("RevitMCP.Server", v26.HandshakeRequest!.ClientName);
        Assert.Equal("0.1.0", v26.HandshakeRequest.ClientVersion);
        Assert.Equal(TimeSpan.FromSeconds(40), v26.LastRunCSharpQueryTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), new ServerTimeouts().CapabilityTimeout);
    }

    [Fact]
    public async Task Two_protocol_26_hosts_require_an_instance_id()
    {
        var outcome = await Service(new RecordingBridgeClientFactory(), ("id-a", "pipe-a", 26), ("id-b", "pipe-b", 26))
            .ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
        Assert.Equal(["id-a", "id-b"], outcome.Candidates!.Select(candidate => candidate.InstanceId).ToArray());
    }

    [Fact]
    public async Task Explicit_protocol_26_host_succeeds_and_forwards_only_document_and_source()
    {
        var client = Client(V26, P26, 26);
        var outcome = await Service(client, V26, P26, 26).ExecuteAsync(V26, Request(), CancellationToken.None);
        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, client.RunCSharpQueryCalls);
        Assert.Equal("doc", client.LastRunCSharpQueryRequest!.DocumentId);
        Assert.Equal(Source, client.LastRunCSharpQueryRequest.Source);
        Assert.Null(client.LastRunCSharpQueryRequest.ExtensionData);
        var forwarded = JsonSerializer.Serialize(client.LastRunCSharpQueryRequest, McpJson.Options);
        Assert.DoesNotContain("instance_id", forwarded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_protocol_25_host_reaches_handshake_and_is_protocol_incompatible()
    {
        var v26 = Client(V26, P26, 26);
        var v25 = Client(V25, P25, 25);
        var factory = Factory(new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal)
        {
            [P26] = v26,
            [P25] = v25
        });
        var outcome = await Service(factory, (V26, P26, 26), (V25, P25, 25)).ExecuteAsync(V25, Request(), CancellationToken.None);

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, outcome.ErrorCode);
        Assert.Equal([P25], factory.RequestedPipes);
        Assert.NotNull(v25.HandshakeRequest);
        Assert.Equal(0, v25.RunCSharpQueryCalls);
        Assert.Equal(0, v26.RunCSharpQueryCalls);
    }

    [Fact]
    public async Task Omitted_instance_with_only_protocol_25_hosts_returns_no_instance()
    {
        var client = Client(V25, P25, 25);
        var factory = Factory(new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal) { [P25] = client });
        var outcome = await Service(factory, (V25, P25, 25)).ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.NoRevitInstance, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
        Assert.Equal(0, client.RunCSharpQueryCalls);
    }

    [Fact]
    public async Task Missing_explicit_instance_is_not_found()
    {
        var outcome = await Service(Client(V26, P26, 26), V26, P26, 26).ExecuteAsync("missing", Request(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
    }

    [Fact]
    public async Task Handshake_identity_mismatch_is_unavailable_and_does_not_call_the_query()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(V26, P26, protocolVersion: 26));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(
                    TestSupport.CreateRegistration("other", P26),
                    selectedProtocolVersion: 26))
            })
        };

        var outcome = await new RunCSharpQueryApplicationService(discovery, factory, new ServerTimeouts())
            .ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].RunCSharpQueryCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task Success_preserves_the_bridge_result_for_every_json_kind(object? marker)
    {
        var json = marker switch
        {
            int value => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string text => text,
            _ => "null"
        };
        var client = Client(V26, P26, 26, Json(json));
        var arguments = Arguments();
        arguments["instance_id"] = Json($"\"{V26}\"");
        var result = await Invoke(client, arguments);

        Assert.False(result.IsError);
        Assert.True(result.StructuredContent.HasValue);
        var structured = result.StructuredContent!.Value;
        Assert.Equal(json, structured.GetProperty("result").GetRawText());
        Assert.Equal(Hash, structured.GetProperty("source_sha256").GetString());
        Assert.Equal(Cache, structured.GetProperty("cache_key").GetString());
        Assert.Equal(1, structured.GetProperty("policy_version").GetInt32());
        Assert.Equal("RevitAPI", structured.GetProperty("revit_api").GetProperty("name").GetString());
        Assert.Equal("26.5.0.0", structured.GetProperty("revit_api").GetProperty("version").GetString());
        Assert.Equal("success", structured.GetProperty("compile_outcome").GetString());
        Assert.Equal(27, structured.GetProperty("duration_ms").GetInt32());
        Assert.Equal(4, structured.GetProperty("result_characters").GetInt32());
        Assert.Equal("doc", client.LastRunCSharpQueryRequest!.DocumentId);
        Assert.Equal(Source, client.LastRunCSharpQueryRequest.Source);
        Assert.DoesNotContain("instance_id", JsonSerializer.Serialize(client.LastRunCSharpQueryRequest, McpJson.Options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CSHARP_QUERY_DISABLED")]
    [InlineData("INVALID_CSHARP_QUERY")]
    [InlineData("CSHARP_QUERY_API_UNSUPPORTED")]
    [InlineData("CSHARP_QUERY_POLICY_REJECTED")]
    [InlineData("CSHARP_QUERY_COMPILE_FAILED")]
    [InlineData("CSHARP_QUERY_COMPILER_FAILED")]
    [InlineData("CSHARP_QUERY_DOCUMENT_NOT_READY")]
    [InlineData("CSHARP_QUERY_BUDGET_EXCEEDED")]
    [InlineData("CSHARP_QUERY_EXECUTION_FAILED")]
    [InlineData("CSHARP_QUERY_INVALID_RESULT")]
    [InlineData("CSHARP_QUERY_RESULT_TOO_LARGE")]
    [InlineData(BridgeErrorCodes.ProtocolIncompatible)]
    [InlineData(CapabilityErrorCodes.ExecutionTimeout)]
    public async Task Accepted_capability_errors_pass_through_the_application_service(string code)
    {
        var client = Client(V26, P26, 26);
        client.RunCSharpQuery = (_, _, _) => throw new BridgeException(code, "The C# query could not be completed.");
        var outcome = await Service(client, V26, P26, 26).ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(code, outcome.ErrorCode);
        Assert.Equal("The C# query could not be completed.", outcome.ErrorMessage);
        Assert.Null(outcome.Details);
    }

    [Fact]
    public async Task Policy_rejection_keeps_bounded_diagnostics_and_drops_forbidden_fields()
    {
        var details = Json(
            """
            {
              "source": "return Environment.TickCount;",
              "cache_key": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
              "path": "C:\\secret\\RevitAPI.dll",
              "stack_trace": "at GeneratedQuery.Execute",
              "unexpected": "leak",
              "source_sha256": "ca37dfdba83278d4cd4babaefd2c7a52ef7c9ad5606339fcae2cd3e97c5f3fde",
              "policy_version": 1,
              "revit_api": { "name": "RevitAPI", "version": "26.5.0.0", "path": "C:\\secret\\RevitAPI.dll" },
              "diagnostics": [
                { "code": "CSHARP_QUERY_POLICY_REJECTED", "line": 1, "column": 8, "message": "Environment is outside the read-only query policy.", "source": "return Environment.TickCount;" },
                { "code": "CSHARP_QUERY_POLICY_REJECTED", "line": 1, "column": 20, "message": "Environment is outside the read-only query policy.\r\nsecret" }
              ],
              "compile_outcome": "policy_rejected"
            }
            """);
        var client = Client(V26, P26, 26);
        client.RunCSharpQuery = (_, _, _) => throw new BridgeException(
            "CSHARP_QUERY_POLICY_REJECTED",
            "The C# query could not be completed.",
            details);
        var result = await Invoke(client, Arguments());
        var payload = ErrorPayload(result);
        Assert.Equal("CSHARP_QUERY_POLICY_REJECTED", payload.GetProperty("code").GetString());
        Assert.Equal("The C# query could not be completed.", payload.GetProperty("message").GetString());
        var sanitized = payload.GetProperty("details");
        Assert.Equal("policy_rejected", sanitized.GetProperty("compile_outcome").GetString());
        Assert.Equal(1, sanitized.GetProperty("policy_version").GetInt32());
        Assert.Equal("RevitAPI", sanitized.GetProperty("revit_api").GetProperty("name").GetString());
        Assert.Equal("26.5.0.0", sanitized.GetProperty("revit_api").GetProperty("version").GetString());
        Assert.False(sanitized.GetProperty("revit_api").TryGetProperty("path", out _));
        var diagnostic = Assert.Single(sanitized.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("CSHARP_QUERY_POLICY_REJECTED", diagnostic.GetProperty("code").GetString());
        Assert.Equal(8, diagnostic.GetProperty("column").GetInt32());
        var raw = payload.GetRawText();
        Assert.DoesNotContain("return Environment.TickCount;", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("cache_key", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("stack_trace", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("unexpected", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_or_only_forbidden_details_are_omitted()
    {
        var client = Client(V26, P26, 26);
        client.RunCSharpQuery = (_, _, _) => throw new BridgeException(
            "CSHARP_QUERY_DISABLED",
            "The C# query could not be completed.",
            Json("""{"source":"secret","cache_key":"x","path":"C:\\\\secret","stack_trace":"at x","unexpected":"y"}"""));
        var result = await Invoke(client, Arguments());
        var payload = ErrorPayload(result);
        Assert.Equal("CSHARP_QUERY_DISABLED", payload.GetProperty("code").GetString());
        Assert.False(payload.TryGetProperty("details", out _));
        Assert.DoesNotContain("secret", payload.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Document_reason_budget_invalid_result_and_size_keep_sanitized_details()
    {
        await AssertDetail("CSHARP_QUERY_DOCUMENT_NOT_READY", """{"reason":"document_mismatch","source":"body"}""", "reason", "document_mismatch");
        await AssertDetail("CSHARP_QUERY_BUDGET_EXCEEDED", """{"compile_outcome":"success","cache_key":"nope"}""", "compile_outcome", "success");
        await AssertDetail("CSHARP_QUERY_INVALID_RESULT", """{"compile_outcome":"success"}""", "compile_outcome", "success");
        await AssertDetail("CSHARP_QUERY_RESULT_TOO_LARGE", """{"compile_outcome":"success"}""", "compile_outcome", "success");
        await AssertDetail("CSHARP_QUERY_COMPILE_FAILED", """{"compile_outcome":"compile_failed"}""", "compile_outcome", "compile_failed");
    }

    [Fact]
    public async Task Outer_timeout_uses_the_dedicated_budget_and_passes_the_bridge_timeout_code()
    {
        var client = Client(V26, P26, 26);
        client.RunCSharpQuery = (_, timeout, _) =>
        {
            Assert.Equal(TimeSpan.FromSeconds(40), timeout);
            throw new BridgeException(CapabilityErrorCodes.ExecutionTimeout, "The Revit C# query request timed out.");
        };
        var outcome = await Service(client, V26, P26, 26).ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, outcome.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(40), client.LastRunCSharpQueryTimeout);
    }

    [Fact]
    public async Task Unexpected_transport_failure_stays_unavailable()
    {
        var client = Client(V26, P26, 26);
        client.RunCSharpQuery = (_, _, _) => throw new IOException("C:\\secret\\pipe");
        var outcome = await Service(client, V26, P26, 26).ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.DoesNotContain("secret", outcome.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_errors_without_details_serialize_code_and_message_only()
    {
        var result = McpCallResultFactory.Error(McpToolErrorCodes.NoRevitInstance, ToolErrorMessages.NoRevitInstance);
        var payload = ErrorPayload(result);
        Assert.Equal(["code", "message"], payload.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(McpToolErrorCodes.NoRevitInstance, payload.GetProperty("code").GetString());
        Assert.Equal(ToolErrorMessages.NoRevitInstance, payload.GetProperty("message").GetString());
    }

    [Fact]
    public void Production_list_is_23_and_the_dependency_boundary_holds()
    {
        var root = FindRepoRoot();
        var serverHost = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Server", "ServerHost.cs"));
        Assert.Equal(23, System.Text.RegularExpressions.Regex.Matches(serverHost, @"\.With\w+Tool\(\)").Count);
        Assert.Contains(".WithRunCSharpQueryTool()", serverHost, StringComparison.Ordinal);
        Assert.Equal(26, BridgeProtocol.CurrentVersion);

        var server = string.Join('\n', Directory.GetFiles(Path.Combine(root, "src", "BimIt.Revit.Server"), "*.cs").Select(File.ReadAllText));
        var project = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Server", "BimIt.Revit.Server.csproj"));
        Assert.DoesNotContain("RevitAPI", project, StringComparison.Ordinal);
        Assert.DoesNotContain("using Autodesk", server, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "BimIt.Revit.Compiler", "CompilerHost", "Microsoft.CodeAnalysis", "Roslyn" })
        {
            Assert.DoesNotContain(forbidden, server, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, project, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("revit.run_csharp_query", serverHost, StringComparison.Ordinal);
        var assistant = string.Join(
            '\n',
            Directory.GetFiles(Path.Combine(root, "src", "BimIt.Revit.Assistant"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.True(System.Text.RegularExpressions.Regex.Matches(assistant, "revit_run_csharp_query").Count == 1);
        Assert.Contains("\"revit_run_csharp_query\"", assistant, StringComparison.Ordinal);
        Assert.DoesNotContain("RunCSharpQueryMcpTools", assistant, StringComparison.Ordinal);
    }

    private static async Task AssertDetail(string code, string details, string property, string expected)
    {
        var client = Client(V26, P26, 26);
        client.RunCSharpQuery = (_, _, _) => throw new BridgeException(code, "The C# query could not be completed.", Json(details));
        var outcome = await Service(client, V26, P26, 26).ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(code, outcome.ErrorCode);
        Assert.NotNull(outcome.Details);
        Assert.Equal(expected, outcome.Details.Value.GetProperty(property).GetString());
        Assert.DoesNotContain("cache_key", outcome.Details.Value.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("source", outcome.Details.Value.GetRawText(), StringComparison.Ordinal);
    }

    private static void AssertInvalidRequest(CallToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Equal(McpToolErrorCodes.InvalidRequest, ErrorPayload(result).GetProperty("code").GetString());
    }

    private static JsonElement ErrorPayload(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static void AssertOrder(string description, IReadOnlyList<string> fragments)
    {
        var previous = -1;
        foreach (var fragment in fragments)
        {
            var index = description.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(index > previous, $"'{fragment}' is missing or out of order.");
            previous = index;
        }
    }

    private static string[] Strings(JsonElement array)
    {
        return array.EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    private static string[] Names(JsonElement properties)
    {
        return properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<CallToolResult> Invoke(RecordingBridgeClient client, IDictionary<string, JsonElement> arguments)
    {
        var tool = RunCSharpQueryToolRegistration.Create(new RunCSharpQueryMcpTools(Service(client, V26, P26, 26)));
        return await McpToolInvoke.InvokeAsync(tool, arguments);
    }

    private static ModelContextProtocol.Server.McpServerTool Tool()
    {
        return RunCSharpQueryToolRegistration.Create(new RunCSharpQueryMcpTools(Service(Client(V26, P26, 26), V26, P26, 26)));
    }

    private static RunCSharpQueryApplicationService Service(RecordingBridgeClient client, string instanceId, string pipe, int protocol)
    {
        return Service(Factory(new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal) { [pipe] = client }), (instanceId, pipe, protocol));
    }

    private static RunCSharpQueryApplicationService Service(
        RecordingBridgeClientFactory factory,
        params (string InstanceId, string Pipe, int ProtocolVersion)[] instances)
    {
        var discovery = new FakeDiscovery();
        foreach (var instance in instances)
        {
            discovery.Instances.Add(TestSupport.Ready(instance.InstanceId, instance.Pipe, protocolVersion: instance.ProtocolVersion));
        }

        return new RunCSharpQueryApplicationService(discovery, factory, new ServerTimeouts());
    }

    private static RecordingBridgeClientFactory Factory(IReadOnlyDictionary<string, RecordingBridgeClient> clients)
    {
        return new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) => Task.FromResult(clients[pipe])
        };
    }

    private static RecordingBridgeClient Client(string instanceId, string pipe, int version, JsonElement? result = null)
    {
        var registration = TestSupport.CreateRegistration(instanceId, pipe);
        var value = result ?? Json("1");
        return new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: version)),
            RunCSharpQuery = (_, _, _) => Task.FromResult(Result(value))
        };
    }

    private static RunCSharpQueryResult Result(JsonElement result)
    {
        return new RunCSharpQueryResult
        {
            Result = result,
            SourceSha256 = Hash,
            CacheKey = Cache,
            PolicyVersion = 1,
            RevitApi = new RunCSharpQueryApiIdentity { Name = "RevitAPI", Version = "26.5.0.0" },
            CompileOutcome = "success",
            DurationMs = 27,
            ResultCharacters = 4
        };
    }

    private static RunCSharpQueryRequest Request()
    {
        return new RunCSharpQueryRequest { DocumentId = "doc", Source = Source };
    }

    private static Dictionary<string, JsonElement> Arguments()
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\""),
            ["source"] = Json($"\"{Source}\"")
        };
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
