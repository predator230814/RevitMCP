using System.Globalization;
using System.Text.Json;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal sealed class ExperimentalRevitApiLookupReadOperation : RevitReadOperation<ApiMetadataLookupRequest, ApiMetadataResult>
{
    internal const string OperationId = "experimental.revit_api_lookup";

    internal const int Version = 1;

    private const string InputSchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "query": { "type": "string", "minLength": 2, "maxLength": 128 },
            "type_name": { "type": "string", "minLength": 1, "maxLength": 256 }
          }
        }
        """;

    private const string OutputSchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary", "returned_count", "types"],
          "properties": {
            "summary": {
              "type": "object",
              "additionalProperties": false,
              "required": ["mode", "matched_type_count"],
              "properties": {
                "mode": { "type": "string", "enum": ["search", "exact"] },
                "matched_type_count": { "type": "integer", "minimum": 0 }
              }
            },
            "returned_count": { "type": "integer", "minimum": 0 },
            "types": {
              "type": "array",
              "maxItems": 200,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["type_name", "is_element", "is_abstract", "can_collect", "matched_members", "properties", "properties_truncated", "methods", "methods_truncated"],
                "properties": {
                  "type_name": { "type": "string" },
                  "base_type_name": { "type": "string" },
                  "is_element": { "type": "boolean" },
                  "is_abstract": { "type": "boolean" },
                  "can_collect": { "type": "boolean" },
                  "matched_members": {
                    "type": "array",
                    "maxItems": 8,
                    "items": { "type": "string" }
                  },
                  "properties": {
                    "type": "array",
                    "maxItems": 80,
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["name", "return_type", "can_project"],
                      "properties": {
                        "name": { "type": "string" },
                        "return_type": { "type": "string" },
                        "can_project": { "type": "boolean" },
                        "rejection": { "type": "string" }
                      }
                    }
                  },
                  "properties_truncated": { "type": "boolean" },
                  "methods": {
                    "type": "array",
                    "maxItems": 40,
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["name", "return_type", "parameter_count", "executable"],
                      "properties": {
                        "name": { "type": "string" },
                        "return_type": { "type": "string" },
                        "parameter_count": { "type": "integer", "minimum": 0 },
                        "executable": { "type": "boolean", "const": false }
                      }
                    }
                  },
                  "methods_truncated": { "type": "boolean" }
                }
              }
            }
          }
        }
        """;

    public ExperimentalRevitApiLookupReadOperation()
    {
        Descriptor = new ReadOperationDescriptor
        {
            Id = OperationId,
            ContractVersion = Version,
            Title = "Revit API metadata",
            Description = "Searches public types and members of the Revit API assembly already loaded by this Revit process. Pass query for a bounded substring search, or type_name for one type and its members. Search results omit member schemas. Methods are informational and are not executable. This operation does not read model elements.",
            Context = "document",
            Effects = "read",
            Pagination = "cursor",
            InputSchema = Parse(InputSchemaJson),
            OutputSchema = Parse(OutputSchemaJson)
        };
    }

    public override ReadOperationDescriptor Descriptor { get; }

    internal static byte[] FingerprintFor(ApiMetadataLookupRequest request, string documentId, ReadOperationDescriptor descriptor)
    {
        return new DeclarativeQueryFingerprintBuilder(descriptor.Id)
            .Value(descriptor.ContractVersion.ToString(CultureInfo.InvariantCulture))
            .Value(documentId)
            .Value(request.Query)
            .Value(request.TypeName)
            .Compute();
    }

    protected override void ValidateRequest(ApiMetadataLookupRequest request)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);
        var hasType = !string.IsNullOrWhiteSpace(request.TypeName);
        if (hasQuery == hasType)
        {
            throw InvalidInput("Pass query or type_name, and not both.");
        }
    }

    protected override byte[] CreateFingerprint(ApiMetadataLookupRequest request, string documentId, ReadOperationDescriptor descriptor)
    {
        return FingerprintFor(request, documentId, descriptor);
    }

    protected override ReadOperationExecutionResult ExecuteRead(ApiMetadataLookupRequest request, ReadOperationExecutionContext context)
    {
        if (context.Host is not RevitActiveReadDocument active)
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The read operation could not be read.");
        }

        var matched = DocumentModificationGuard.Read(active.Document, () => Find(request));
        var page = ApiAccessSpikePages.Slice(matched, context.Offset, context.PageSize);
        return ApiAccessSpikePages.Complete(
            context,
            matched.Count,
            page.Cast<object>().ToArray(),
            rows => Shape(request.TypeName is null ? "search" : "exact", matched.Count, rows.Cast<ApiMetadataType>().ToArray()),
            FingerprintFor(request, context.DocumentId, context.Descriptor));
    }

    protected override void ValidateTypedResult(ApiMetadataResult result)
    {
        if (result.Summary is null
            || result.Types is null
            || result.ReturnedCount != result.Types.Count
            || result.Summary.MatchedTypeCount < result.ReturnedCount
            || (result.Summary.Mode != "search" && result.Summary.Mode != "exact"))
        {
            throw InvalidResult();
        }

        foreach (var type in result.Types)
        {
            if (type is null
                || string.IsNullOrEmpty(type.TypeName)
                || type.MatchedMembers is null
                || type.Properties is null
                || type.Methods is null
                || type.MatchedMembers.Count > RevitApiMetadataCatalog.MaxMatchedMembers
                || type.Properties.Count > RevitApiMetadataCatalog.MaxListedProperties
                || type.Methods.Count > RevitApiMetadataCatalog.MaxListedMethods)
            {
                throw InvalidResult();
            }

            foreach (var method in type.Methods)
            {
                if (method is null || method.Executable || string.IsNullOrEmpty(method.Name))
                {
                    throw InvalidResult();
                }
            }
        }
    }

    private static IReadOnlyList<ApiMetadataType> Find(ApiMetadataLookupRequest request)
    {
        if (request.TypeName is not null)
        {
            var type = TrustedRevitApiTypes.FindPublicType(request.TypeName);
            if (type is null)
            {
                throw new BridgeException(
                    CapabilityErrorCodes.InvalidReadOperationInput,
                    "type_name is not a public type in the loaded Revit API assembly.");
            }

            return [RevitApiMetadataCatalog.Describe(type)];
        }

        return RevitApiMetadataCatalog.Search(request.Query ?? string.Empty);
    }

    private static ApiMetadataResult Shape(string mode, int matchedCount, IReadOnlyList<ApiMetadataType> types)
    {
        return new ApiMetadataResult
        {
            Summary = new ApiMetadataSummary
            {
                Mode = mode,
                MatchedTypeCount = matchedCount
            },
            ReturnedCount = types.Count,
            Types = types
        };
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
