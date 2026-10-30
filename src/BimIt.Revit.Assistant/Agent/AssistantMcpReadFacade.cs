using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant;

/// <summary>
/// One generic Revit MCP read as the provider sees it: the Server's own name and description, and the Server's input
/// schema without the trusted binding arguments.
/// </summary>
internal sealed record AssistantReadTool(
    string Name,
    string Description,
    JsonElement ProviderSchema,
    IReadOnlySet<string> Properties,
    bool InjectsDocument,
    bool RequiresDocument);

/// <summary>
/// Decides which Server tools are trusted generic reads. The decision comes from the Server's tools/list metadata, not
/// from a list of names, and fails closed: a tool is exposed only when every rule holds.
/// </summary>
internal static partial class AssistantReadToolPolicy
{
    public const string InstanceArgument = "instance_id";

    public const string DocumentArgument = "document_id";

    public static IReadOnlySet<string> TrustedReadNames(IEnumerable<AssistantMcpToolDescriptor>? descriptors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors ?? [])
        {
            if (TryCreate(descriptor, out var tool, out _))
            {
                names.Add(tool!.Name);
            }
        }

        return names;
    }

    public static bool TryCreate(AssistantMcpToolDescriptor descriptor, out AssistantReadTool? tool, out string reason)
    {
        tool = null;
        var name = descriptor?.Name ?? "";
        if (descriptor is null || !ReadName().IsMatch(name))
        {
            reason = "name";
            return false;
        }

        if (AssistantTools.IsTrustedWriteTool(name) || BimItAgentReadTools.DeniedTools.Contains(name, StringComparer.Ordinal))
        {
            reason = "trusted_write";
            return false;
        }

        if (descriptor.ReadOnlyHint != true)
        {
            reason = "not_read_only";
            return false;
        }

        if (descriptor.DestructiveHint == true)
        {
            reason = "destructive";
            return false;
        }

        // MCP treats a missing openWorldHint as open world. Only an explicit false is trusted.
        if (descriptor.OpenWorldHint != false)
        {
            reason = "open_world";
            return false;
        }

        if (!TryProviderSchema(descriptor.InputSchema, out var schema, out var properties, out var hasDocument, out var requiresDocument, out reason))
        {
            return false;
        }

        var description = AssistantSafeText.Clean(descriptor.Description, 2_000);
        if (string.IsNullOrWhiteSpace(description))
        {
            reason = "description";
            return false;
        }

        tool = new AssistantReadTool(descriptor.Name, description, schema, properties, hasDocument, requiresDocument);
        reason = "";
        return true;
    }

    /// <summary>
    /// Removes <c>instance_id</c> and <c>document_id</c> from the Server schema's properties and required list. Every
    /// other keyword, property, and requirement is kept as the Server published it.
    /// </summary>
    public static bool TryProviderSchema(
        JsonElement serverSchema,
        out JsonElement providerSchema,
        out IReadOnlySet<string> properties,
        out bool hasDocument,
        out bool requiresDocument,
        out string reason)
    {
        providerSchema = default;
        properties = new HashSet<string>(StringComparer.Ordinal);
        hasDocument = false;
        requiresDocument = false;
        reason = "schema";
        if (serverSchema.ValueKind != JsonValueKind.Object
            || !serverSchema.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || type.GetString() != "object"
            || !serverSchema.TryGetProperty("properties", out var serverProperties)
            || serverProperties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // The exact Revit instance binding must be an argument BIM-IT can set. Without it the call is not bindable.
        if (!serverProperties.TryGetProperty(InstanceArgument, out _))
        {
            reason = "no_instance_binding";
            return false;
        }

        var required = new List<string>();
        if (serverSchema.TryGetProperty("required", out var requiredElement))
        {
            if (requiredElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in requiredElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                required.Add(item.GetString()!);
            }
        }

        var root = JsonNode.Parse(serverSchema.GetRawText())!.AsObject();
        var propertyNode = root["properties"]!.AsObject();
        hasDocument = propertyNode.ContainsKey(DocumentArgument);
        requiresDocument = required.Contains(DocumentArgument, StringComparer.Ordinal);
        propertyNode.Remove(InstanceArgument);
        propertyNode.Remove(DocumentArgument);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in propertyNode)
        {
            if (property.Value is not JsonObject)
            {
                return false;
            }

            names.Add(property.Key);
        }

        var providerRequired = required.Where(name => name is not InstanceArgument and not DocumentArgument).ToArray();
        if (providerRequired.Any(name => !names.Contains(name)))
        {
            return false;
        }

        root.Remove("required");
        if (providerRequired.Length > 0)
        {
            root["required"] = new JsonArray(providerRequired.Select(name => (JsonNode)JsonValue.Create(name)!).ToArray());
        }

        providerSchema = JsonSerializer.SerializeToElement(root);
        properties = names;
        reason = "";
        return true;
    }

    [GeneratedRegex("^revit_[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex ReadName();
}

/// <summary>
/// The provider-neutral read facade. The model calls a generic Revit MCP read by its MCP name with the Server's own
/// arguments, minus the trusted binding. BIM-IT injects the exact <c>instance_id</c> and, when the tool takes one, the
/// active <c>document_id</c> read fresh from <c>revit_get_context</c> for that call. Only trusted reads are reachable.
/// </summary>
internal sealed class AssistantMcpReadFacade
{
    private readonly IAssistantToolSession _session;
    private readonly string _instanceId;
    private readonly Dictionary<string, AssistantReadTool> _tools = new(StringComparer.Ordinal);
    private readonly object _usedLock = new();
    private readonly List<string> _used = [];

    public AssistantMcpReadFacade(IAssistantToolSession session, string instanceId, IReadOnlyList<AssistantMcpToolDescriptor>? descriptors)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("An exact instance_id is required.", nameof(instanceId));
        }

        _instanceId = instanceId;
        var rejected = new List<string>();
        foreach (var descriptor in descriptors ?? [])
        {
            if (AssistantReadToolPolicy.TryCreate(descriptor, out var tool, out var reason))
            {
                _tools[tool!.Name] = tool;
            }
            else
            {
                rejected.Add(descriptor.Name + ":" + reason);
            }
        }

        Rejected = rejected;
    }

    public IReadOnlyList<AssistantReadTool> Tools => _tools.Values.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();

    /// <summary>Server tools that were not exposed, with the policy reason. Diagnostic only.</summary>
    public IReadOnlyList<string> Rejected { get; }

    public bool IsExposed(string? name) => name is not null && _tools.ContainsKey(name);

    public IReadOnlyList<string> ConsumeUsedTools()
    {
        lock (_usedLock)
        {
            var copy = _used.ToArray();
            _used.Clear();
            return copy;
        }
    }

    public async Task<IReadOnlyDictionary<string, object?>> InvokeAsync(
        string name,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        if (AssistantTools.IsTrustedWriteTool(name) || !_tools.TryGetValue(name, out var tool))
        {
            return Fail("tool_not_allowed", "That tool is not available to BIM-IT Assistant.");
        }

        Record(name);
        var call = new Dictionary<string, object?>(StringComparer.Ordinal) { [AssistantReadToolPolicy.InstanceArgument] = _instanceId };
        foreach (var (key, value) in arguments ?? new Dictionary<string, object?>())
        {
            if (key is AssistantReadToolPolicy.InstanceArgument or AssistantReadToolPolicy.DocumentArgument)
            {
                return Fail(
                    "argument_not_allowed",
                    "BIM-IT supplies instance_id and document_id for the current Revit session. Call the tool again without them.");
            }

            if (!tool.Properties.Contains(key))
            {
                return Fail("invalid_arguments", "The argument '" + AssistantSafeText.Clean(key, 64) + "' is not part of " + name + ".");
            }

            call[key] = value;
        }

        if (tool.InjectsDocument && name != AssistantTools.GetContext)
        {
            var context = await _session.CallAsync(
                AssistantTools.GetContext,
                new Dictionary<string, object?> { [AssistantReadToolPolicy.InstanceArgument] = _instanceId },
                cancellationToken).ConfigureAwait(false);
            if (context.IsError)
            {
                return Fail(context);
            }

            if (!AssistantWorkflows.TryReadContext(context.Structured, out var documentPresent, out _, out var documentId))
            {
                return Fail("malformed", "The current Revit context could not be read.");
            }

            if (!documentPresent && tool.RequiresDocument)
            {
                return Fail(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is open, so " + name + " was not called.");
            }

            if (documentPresent && documentId is null && tool.RequiresDocument)
            {
                return Fail(
                    "bridge_v12_required",
                    "The connected Revit host did not report the active document_id. No document id was invented, and " + name + " was not called.");
            }

            if (documentId is not null)
            {
                call[AssistantReadToolPolicy.DocumentArgument] = documentId;
            }
        }

        var result = await _session.CallAsync(name, call, cancellationToken).ConfigureAwait(false);
        if (result.IsError)
        {
            return Fail(result);
        }

        if (result.Structured is not { ValueKind: JsonValueKind.Object or JsonValueKind.Array } structured)
        {
            return Fail("malformed", "The " + name + " result could not be read.");
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["result"] = JsonNode.Parse(structured.GetRawText()),
        };
    }

    private void Record(string name)
    {
        lock (_usedLock)
        {
            if (!_used.Contains(name, StringComparer.Ordinal))
            {
                _used.Add(name);
            }
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
}
