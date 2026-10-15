using System.Text.Json;
using System.Text.Json.Nodes;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant;

internal sealed class BimItAgentReadTools
{
    public const string ModelContext = "bimit_get_model_context";

    public const string Warnings = "bimit_get_warnings";

    public const string ActiveViewElements = "bimit_get_active_view_elements";

    public const string InspectSelection = "bimit_inspect_selection";

    public const string ExploreSelectedMep = "bimit_explore_selected_mep";

    public const int ActiveViewLimit = 25;

    public const int InspectSelectionLimit = 10;

    public const int ExploreSelectionLimit = 3;

    public const int ExploreTopologyMaxDepth = 2;

    public const int ExploreTopologyMaxElements = 25;

    public const int ExploreTopologyMaxEdges = 40;

    public const int ExploreDetailsLimit = 10;

    public static readonly string[] Names = [ModelContext, Warnings, ActiveViewElements, InspectSelection, ExploreSelectedMep];

    private static readonly string[] DeniedTools =
    [
        "revit_preview_parameter_updates",
        "revit_request_parameter_update_review",
        "revit_apply_parameter_updates",
        "runCSharp",
        "runPython",
        "executeRevitApi",
        "shell",
    ];

    private readonly object _usedLock = new();
    private readonly List<string> _used = [];
    private readonly IAssistantToolSession _session;
    private readonly string _instanceId;

    public BimItAgentReadTools(IAssistantToolSession session, string instanceId)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("An exact instance_id is required.", nameof(instanceId));
        }

        _instanceId = instanceId;
    }

    public static bool IsExposed(string? toolName)
    {
        return toolName is not null && Names.Contains(toolName, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> ConsumeUsedTools()
    {
        lock (_usedLock)
        {
            var copy = _used.ToArray();
            _used.Clear();
            return copy;
        }
    }

    public async Task<IReadOnlyDictionary<string, object?>> InvokeAsync(string toolName, CancellationToken cancellationToken)
    {
        if (!IsExposed(toolName) || DeniedTools.Contains(toolName, StringComparer.Ordinal))
        {
            return Fail("tool_not_allowed", "That tool is not available to BIM-IT Assistant.");
        }

        return toolName switch
        {
            ModelContext => await ModelContextAsync(cancellationToken).ConfigureAwait(false),
            Warnings => await WarningsAsync(cancellationToken).ConfigureAwait(false),
            ActiveViewElements => await ActiveViewAsync(cancellationToken).ConfigureAwait(false),
            InspectSelection => await InspectSelectionAsync(cancellationToken).ConfigureAwait(false),
            ExploreSelectedMep => await ExploreSelectedMepAsync(cancellationToken).ConfigureAwait(false),
            _ => Fail("tool_not_allowed", "That tool is not available to BIM-IT Assistant."),
        };
    }

    private async Task<IReadOnlyDictionary<string, object?>> ModelContextAsync(CancellationToken cancellationToken)
    {
        RecordUse(ModelContext);
        var call = await _session.CallAsync(
            AssistantTools.GetContext,
            new Dictionary<string, object?> { ["instance_id"] = _instanceId },
            cancellationToken).ConfigureAwait(false);
        return Shape(call, "The context result could not be read.");
    }

    private async Task<IReadOnlyDictionary<string, object?>> WarningsAsync(CancellationToken cancellationToken)
    {
        RecordUse(Warnings);
        var context = await ReadContextAsync(cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
        {
            return context.Error;
        }

        if (!context.DocumentPresent)
        {
            return Fail(CapabilityErrorCodes.NoActiveDocument, "No active document is open, so warnings were not requested.");
        }

        if (context.DocumentId is null)
        {
            return Fail(
                "bridge_v12_required",
                "The connected BIM-IT Revit MCP host did not include document_id on the active document. Bridge protocol v12 is required. No document id was invented, and revit_get_warnings was not called.");
        }

        var warnings = await _session.CallAsync(
            AssistantTools.GetWarnings,
            new Dictionary<string, object?>
            {
                ["instance_id"] = _instanceId,
                ["document_id"] = context.DocumentId,
            },
            cancellationToken).ConfigureAwait(false);
        return Shape(warnings, "The warnings result could not be read.");
    }

    private async Task<IReadOnlyDictionary<string, object?>> ActiveViewAsync(CancellationToken cancellationToken)
    {
        RecordUse(ActiveViewElements);
        var context = await ReadContextAsync(cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
        {
            return context.Error;
        }

        if (!context.DocumentPresent)
        {
            return Fail(CapabilityErrorCodes.NoActiveDocument, "No active document is open, so elements were not queried.");
        }

        if (!context.ViewPresent)
        {
            return Fail(CapabilityErrorCodes.NoActiveView, "No active view is available, so elements were not queried.");
        }

        var arguments = new Dictionary<string, object?>
        {
            ["instance_id"] = _instanceId,
            ["scope"] = "active_view",
            ["filters"] = new Dictionary<string, object?>(),
            ["limit"] = ActiveViewLimit,
        };
        if (context.DocumentId is not null)
        {
            arguments["document_id"] = context.DocumentId;
        }

        var query = await _session.CallAsync(AssistantTools.QueryElements, arguments, cancellationToken).ConfigureAwait(false);
        return Shape(query, "The query result could not be read.");
    }

    private async Task<IReadOnlyDictionary<string, object?>> InspectSelectionAsync(CancellationToken cancellationToken)
    {
        RecordUse(InspectSelection);
        var selection = await _session.CallAsync(
            AssistantTools.GetSelection,
            new Dictionary<string, object?>
            {
                ["instance_id"] = _instanceId,
                ["limit"] = InspectSelectionLimit,
            },
            cancellationToken).ConfigureAwait(false);
        if (selection.IsError)
        {
            return Fail(
                string.IsNullOrWhiteSpace(selection.ErrorCode) ? "mcp_error" : selection.ErrorCode,
                string.IsNullOrWhiteSpace(selection.ErrorMessage) ? "The MCP tool returned an error." : selection.ErrorMessage);
        }

        if (!TryReadSelection(selection.Structured, out var documentId, out var selectedCount, out var truncated, out var refs))
        {
            return Fail("malformed", "The selection result could not be read.");
        }

        if (refs.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["selected_count"] = selectedCount,
                ["inspected_count"] = 0,
                ["truncated"] = truncated,
                ["elements"] = Array.Empty<IReadOnlyDictionary<string, object?>>(),
            };
        }

        var elements = await _session.CallAsync(
            AssistantTools.GetElements,
            new Dictionary<string, object?>
            {
                ["instance_id"] = _instanceId,
                ["document_id"] = documentId,
                ["element_refs"] = refs,
                ["projection"] = new Dictionary<string, object?>
                {
                    ["fields"] = new[]
                    {
                        "name",
                        "category_name",
                        "family_name",
                        "type_name",
                        "level_name",
                    },
                },
            },
            cancellationToken).ConfigureAwait(false);
        if (elements.IsError)
        {
            return Fail(
                string.IsNullOrWhiteSpace(elements.ErrorCode) ? "mcp_error" : elements.ErrorCode,
                string.IsNullOrWhiteSpace(elements.ErrorMessage) ? "The MCP tool returned an error." : elements.ErrorMessage);
        }

        if (!TryReadElements(elements.Structured, out var items))
        {
            return Fail("malformed", "The element result could not be read.");
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["selected_count"] = selectedCount,
            ["inspected_count"] = items.Count,
            ["truncated"] = truncated,
            ["elements"] = items,
        };
    }

    private async Task<IReadOnlyDictionary<string, object?>> ExploreSelectedMepAsync(CancellationToken cancellationToken)
    {
        RecordUse(ExploreSelectedMep);
        var selection = await _session.CallAsync(
            AssistantTools.GetSelection,
            new Dictionary<string, object?>
            {
                ["instance_id"] = _instanceId,
                ["limit"] = ExploreSelectionLimit,
            },
            cancellationToken).ConfigureAwait(false);
        if (selection.IsError)
        {
            return Fail(selection);
        }

        if (!TryReadSelection(selection.Structured, out var documentId, out var selectedCount, out var selectionTruncated, out var refs))
        {
            return Fail("malformed", "The selection result could not be read.");
        }

        if (refs.Count == 0)
        {
            return EmptyExploration(selectedCount, selectionTruncated);
        }

        var topology = await _session.CallAsync(
            AssistantTools.GetMepTopology,
            new Dictionary<string, object?>
            {
                ["instance_id"] = _instanceId,
                ["document_id"] = documentId,
                ["seed_element_refs"] = refs,
                ["max_depth"] = ExploreTopologyMaxDepth,
                ["max_elements"] = ExploreTopologyMaxElements,
                ["max_edges"] = ExploreTopologyMaxEdges,
            },
            cancellationToken).ConfigureAwait(false);
        if (topology.IsError)
        {
            return Fail(topology);
        }

        if (!TryReadTopology(topology.Structured, out var graph))
        {
            return Fail("malformed", "The topology result could not be read.");
        }

        var items = new List<IReadOnlyDictionary<string, object?>>();
        if (graph.NodeRefs.Count > 0)
        {
            var elements = await _session.CallAsync(
                AssistantTools.GetElements,
                new Dictionary<string, object?>
                {
                    ["instance_id"] = _instanceId,
                    ["document_id"] = documentId,
                    ["element_refs"] = graph.NodeRefs.Take(ExploreDetailsLimit).ToArray(),
                    ["projection"] = new Dictionary<string, object?>
                    {
                        ["fields"] = new[]
                        {
                            "name",
                            "category_name",
                            "family_name",
                            "type_name",
                            "level_name",
                        },
                    },
                },
                cancellationToken).ConfigureAwait(false);
            if (elements.IsError)
            {
                return Fail(elements);
            }

            if (!TryReadElements(elements.Structured, out items))
            {
                return Fail("malformed", "The element result could not be read.");
            }
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["selected_count"] = selectedCount,
            ["seed_count"] = refs.Count,
            ["selection_truncated"] = selectionTruncated,
            ["seeds"] = graph.Seeds,
            ["topology"] = new Dictionary<string, object?>
            {
                ["node_count"] = graph.Nodes.Count,
                ["edge_count"] = graph.Edges.Count,
                ["nodes"] = graph.Nodes,
                ["edges"] = graph.Edges,
                ["truncated"] = graph.Truncated,
                ["truncation_reasons"] = graph.Reasons,
            },
            ["described_count"] = items.Count,
            ["details_truncated"] = graph.Nodes.Count > ExploreDetailsLimit,
            ["elements"] = items,
        };
    }

    private static IReadOnlyDictionary<string, object?> EmptyExploration(int selectedCount, bool selectionTruncated)
    {
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["selected_count"] = selectedCount,
            ["seed_count"] = 0,
            ["selection_truncated"] = selectionTruncated,
            ["seeds"] = Array.Empty<IReadOnlyDictionary<string, object?>>(),
            ["topology"] = new Dictionary<string, object?>
            {
                ["node_count"] = 0,
                ["edge_count"] = 0,
                ["nodes"] = Array.Empty<IReadOnlyDictionary<string, object?>>(),
                ["edges"] = Array.Empty<IReadOnlyDictionary<string, object?>>(),
                ["truncated"] = false,
                ["truncation_reasons"] = Array.Empty<string>(),
            },
            ["described_count"] = 0,
            ["details_truncated"] = false,
            ["elements"] = Array.Empty<IReadOnlyDictionary<string, object?>>(),
        };
    }

    private async Task<ContextRead> ReadContextAsync(CancellationToken cancellationToken)
    {
        var call = await _session.CallAsync(
            AssistantTools.GetContext,
            new Dictionary<string, object?> { ["instance_id"] = _instanceId },
            cancellationToken).ConfigureAwait(false);
        if (call.IsError)
        {
            return new ContextRead(Shape(call, "The context result could not be read."), false, false, null);
        }

        if (!AssistantWorkflows.TryReadContext(call.Structured, out var documentPresent, out var viewPresent, out var documentId))
        {
            return new ContextRead(Fail("malformed", "The context result could not be read."), false, false, null);
        }

        return new ContextRead(null, documentPresent, viewPresent, documentId);
    }

    private static IReadOnlyDictionary<string, object?> Shape(AssistantToolCall call, string malformedMessage)
    {
        if (call.IsError)
        {
            return Fail(
                string.IsNullOrWhiteSpace(call.ErrorCode) ? "mcp_error" : call.ErrorCode,
                string.IsNullOrWhiteSpace(call.ErrorMessage) ? "The MCP tool returned an error." : call.ErrorMessage);
        }

        var result = Clone(call.Structured);
        if (result is null)
        {
            return Fail("malformed", malformedMessage);
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["result"] = result,
        };
    }

    private void RecordUse(string toolName)
    {
        lock (_usedLock)
        {
            if (_used.Count >= AssistantIpc.MaxUsedToolCount || !IsExposed(toolName))
            {
                return;
            }

            _used.Add(toolName);
        }
    }

    private static IReadOnlyDictionary<string, object?> Fail(AssistantToolCall call)
    {
        return Fail(
            string.IsNullOrWhiteSpace(call.ErrorCode) ? "mcp_error" : call.ErrorCode,
            string.IsNullOrWhiteSpace(call.ErrorMessage) ? "The MCP tool returned an error." : call.ErrorMessage);
    }

    private static IReadOnlyDictionary<string, object?> Fail(string code, string message)
    {
        return new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["code"] = code,
            ["message"] = AssistantSafeText.Clean(message, 1_000),
        };
    }

    private static JsonNode? Clone(JsonElement? structured)
    {
        if (structured is not { ValueKind: JsonValueKind.Object or JsonValueKind.Array } json)
        {
            return null;
        }

        return JsonNode.Parse(json.GetRawText());
    }

    private static bool TryReadSelection(
        JsonElement? structured,
        out string documentId,
        out int selectedCount,
        out bool truncated,
        out List<string> refs)
    {
        documentId = "";
        selectedCount = 0;
        truncated = false;
        refs = [];
        if (structured is not { ValueKind: JsonValueKind.Object } json
            || !json.TryGetProperty("context", out var context)
            || context.ValueKind != JsonValueKind.Object
            || !context.TryGetProperty("document_id", out var documentProperty)
            || documentProperty.ValueKind != JsonValueKind.String
            || documentProperty.GetString() is not string document
            || !json.TryGetProperty("selected_count", out var countProperty)
            || !countProperty.TryGetInt32(out selectedCount)
            || selectedCount < 0
            || !json.TryGetProperty("truncated", out var truncatedProperty)
            || truncatedProperty.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || !json.TryGetProperty("element_refs", out var refsProperty)
            || refsProperty.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        truncated = truncatedProperty.GetBoolean();
        foreach (var item in refsProperty.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not string elementRef)
            {
                return false;
            }

            refs.Add(elementRef);
        }

        documentId = document;
        return true;
    }

    private static bool TryReadElements(JsonElement? structured, out List<IReadOnlyDictionary<string, object?>> items)
    {
        items = [];
        if (structured is not { ValueKind: JsonValueKind.Object } json)
        {
            return false;
        }

        GetElementsResult? result;
        try
        {
            result = json.Deserialize<GetElementsResult>(ContractJson.Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (result?.Elements is null)
        {
            return false;
        }

        foreach (var element in result.Elements)
        {
            if (string.IsNullOrEmpty(element.ElementRef) || StatusName(element.Status) is not string status)
            {
                return false;
            }

            items.Add(new Dictionary<string, object?>
            {
                ["element_ref"] = element.ElementRef,
                ["status"] = status,
                ["name"] = Projected(element.Name),
                ["category_name"] = Projected(element.CategoryName),
                ["family_name"] = Projected(element.FamilyName),
                ["type_name"] = Projected(element.TypeName),
                ["level_name"] = Projected(element.LevelName),
            });
        }

        return true;
    }

    private static string? Projected(ProjectedString value)
    {
        return value.IsRequested ? value.Value : null;
    }

    private static bool TryReadTopology(JsonElement? structured, out TopologyView graph)
    {
        graph = new TopologyView([], [], [], [], false, []);
        if (structured is not { ValueKind: JsonValueKind.Object } json)
        {
            return false;
        }

        GetMepTopologyResult? result;
        try
        {
            result = json.Deserialize<GetMepTopologyResult>(ContractJson.Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (result?.Seeds is null || result.Nodes is null || result.Edges is null || result.TruncationReasons is null)
        {
            return false;
        }

        var seeds = new List<IReadOnlyDictionary<string, object?>>(result.Seeds.Count);
        foreach (var seed in result.Seeds)
        {
            if (string.IsNullOrEmpty(seed.ElementRef) || SeedStatusName(seed.Status) is not string status)
            {
                return false;
            }

            seeds.Add(new Dictionary<string, object?>
            {
                ["element_ref"] = seed.ElementRef,
                ["status"] = status,
            });
        }

        var nodes = new List<IReadOnlyDictionary<string, object?>>(result.Nodes.Count);
        var nodeRefs = new List<string>(result.Nodes.Count);
        foreach (var node in result.Nodes)
        {
            if (string.IsNullOrEmpty(node.ElementRef))
            {
                return false;
            }

            nodeRefs.Add(node.ElementRef);
            nodes.Add(new Dictionary<string, object?>
            {
                ["element_ref"] = node.ElementRef,
                ["depth"] = node.Depth,
            });
        }

        var edges = new List<IReadOnlyDictionary<string, object?>>(result.Edges.Count);
        foreach (var edge in result.Edges)
        {
            if (string.IsNullOrEmpty(edge.ElementRefA) || string.IsNullOrEmpty(edge.ElementRefB) || edge.Domains is null)
            {
                return false;
            }

            var domains = new List<string>(edge.Domains.Count);
            foreach (var domain in edge.Domains)
            {
                if (DomainName(domain) is not string name)
                {
                    return false;
                }

                domains.Add(name);
            }

            edges.Add(new Dictionary<string, object?>
            {
                ["element_ref_a"] = edge.ElementRefA,
                ["element_ref_b"] = edge.ElementRefB,
                ["domains"] = domains,
            });
        }

        var reasons = new List<string>(result.TruncationReasons.Count);
        foreach (var reason in result.TruncationReasons)
        {
            if (TruncationReasonName(reason) is not string name)
            {
                return false;
            }

            reasons.Add(name);
        }

        graph = new TopologyView(seeds, nodes, nodeRefs, edges, result.Truncated, reasons);
        return true;
    }

    private static string? StatusName(GetElementResultStatus status)
    {
        return status switch
        {
            GetElementResultStatus.Ok => "ok",
            GetElementResultStatus.NotFound => "not_found",
            _ => null,
        };
    }

    private static string? SeedStatusName(MepTopologySeedStatus status)
    {
        return status switch
        {
            MepTopologySeedStatus.Ok => "ok",
            MepTopologySeedStatus.NotFound => "not_found",
            MepTopologySeedStatus.NoConnectors => "no_connectors",
            _ => null,
        };
    }

    private static string? DomainName(MepTopologyDomain domain)
    {
        return domain switch
        {
            MepTopologyDomain.Hvac => "hvac",
            MepTopologyDomain.Piping => "piping",
            MepTopologyDomain.Electrical => "electrical",
            MepTopologyDomain.CableTrayConduit => "cable_tray_conduit",
            _ => null,
        };
    }

    private static string? TruncationReasonName(MepTopologyTruncationReason reason)
    {
        return reason switch
        {
            MepTopologyTruncationReason.Depth => "depth",
            MepTopologyTruncationReason.Elements => "elements",
            MepTopologyTruncationReason.Edges => "edges",
            _ => null,
        };
    }

    private sealed record TopologyView(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Seeds,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Nodes,
        IReadOnlyList<string> NodeRefs,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Edges,
        bool Truncated,
        IReadOnlyList<string> Reasons);

    private sealed record ContextRead(
        IReadOnlyDictionary<string, object?>? Error,
        bool DocumentPresent,
        bool ViewPresent,
        string? DocumentId);
}
