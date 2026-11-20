using System.Globalization;
using System.Text.Json;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// CAP-0017 <c>revit_api.metadata</c> version 1.
/// Search and exact metadata do not read elements.
/// </summary>
internal sealed class RevitApiMetadataReadOperation : RevitReadOperation<RevitApiMetadataRequest, RevitApiMetadataResult>
{
    internal const string OperationId = "revit_api.metadata";

    internal const int Version = 1;

    internal const string OperationTitle = "Revit API element metadata";

    internal const string OperationDescription =
        "Searches public Element subclasses and their public instance properties in the Revit API assembly already loaded by this Revit process. Pass query or type_name, not both. Search returns type summaries and matched property names. Exact type_name returns property metadata. Methods are not listed. This operation does not read model elements and does not invoke property getters.";

    internal const string InputSchemaJson =
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

    internal const string OutputSchemaJson =
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
                "required": ["type_name", "is_element", "is_abstract", "matched_members", "matched_members_omitted_count", "properties", "rejected_omitted_count"],
                "properties": {
                  "type_name": { "type": "string", "minLength": 1 },
                  "base_type_name": { "type": "string", "minLength": 1 },
                  "is_element": { "type": "boolean" },
                  "is_abstract": { "type": "boolean" },
                  "matched_members": {
                    "type": "array",
                    "maxItems": 8,
                    "items": { "type": "string", "minLength": 1 }
                  },
                  "matched_members_omitted_count": { "type": "integer", "minimum": 0 },
                  "properties": {
                    "type": "array",
                    "maxItems": 80,
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["name", "return_type", "can_project"],
                      "properties": {
                        "name": { "type": "string", "minLength": 1 },
                        "return_type": { "type": "string", "minLength": 1 },
                        "can_project": { "type": "boolean" },
                        "rejection": { "type": "string", "minLength": 1 }
                      }
                    }
                  },
                  "rejected_omitted_count": { "type": "integer", "minimum": 0 }
                }
              }
            }
          }
        }
        """;

    private static readonly HashSet<string> Rejections = new(StringComparer.Ordinal)
    {
        RevitApiPropertyPolicy.NotReadable,
        RevitApiPropertyPolicy.Indexer,
        RevitApiPropertyPolicy.StaticProperty,
        RevitApiPropertyPolicy.UnsupportedReturn,
        RevitApiPropertyPolicy.OutsideAssembly
    };

    public RevitApiMetadataReadOperation()
    {
        Descriptor = new ReadOperationDescriptor
        {
            Id = OperationId,
            ContractVersion = Version,
            Title = OperationTitle,
            Description = OperationDescription,
            Context = "document",
            Effects = "read",
            Pagination = "cursor",
            InputSchema = Parse(InputSchemaJson),
            OutputSchema = Parse(OutputSchemaJson)
        };
    }

    public override ReadOperationDescriptor Descriptor { get; }

    protected override void ValidateRequest(RevitApiMetadataRequest request)
    {
        var hasQuery = request.Query is not null;
        var hasTypeName = request.TypeName is not null;
        if (hasQuery == hasTypeName)
        {
            throw InvalidInput("Pass query or type_name, not both.");
        }
    }

    protected override byte[] CreateFingerprint(RevitApiMetadataRequest request, string documentId, ReadOperationDescriptor descriptor)
    {
        var builder = new DeclarativeQueryFingerprintBuilder(descriptor.Id)
            .Value(descriptor.ContractVersion.ToString(CultureInfo.InvariantCulture))
            .Value(documentId);
        if (request.Query is not null)
        {
            builder.Value("search").Value(request.Query);
        }
        else
        {
            builder.Value("exact").Value(request.TypeName);
        }

        return builder.Compute();
    }

    protected override ReadOperationExecutionResult ExecuteRead(RevitApiMetadataRequest request, ReadOperationExecutionContext context)
    {
        var fingerprint = CreateFingerprint(request, context.DocumentId, context.Descriptor);
        if (request.TypeName is not null)
        {
            var described = RevitApiMetadataIndex.DescribeExact(request.TypeName);
            return RevitApiReadPages.Complete(
                context,
                1,
                new[] { described },
                slice => new RevitApiMetadataResult
                {
                    Summary = new RevitApiMetadataSummary { Mode = "exact", MatchedTypeCount = 1 },
                    ReturnedCount = slice.Count,
                    Types = slice
                },
                fingerprint);
        }

        var matches = RevitApiMetadataIndex.Search(request.Query!);
        var page = RevitApiReadPages.Slice(matches, context.Offset, context.PageSize);
        return RevitApiReadPages.Complete(
            context,
            matches.Count,
            page,
            slice => new RevitApiMetadataResult
            {
                Summary = new RevitApiMetadataSummary { Mode = "search", MatchedTypeCount = matches.Count },
                ReturnedCount = slice.Count,
                Types = slice
            },
            fingerprint);
    }

    protected override void ValidateTypedResult(RevitApiMetadataResult result)
    {
        if (result.Summary is null
            || result.Types is null
            || result.ReturnedCount != result.Types.Count
            || result.Summary.MatchedTypeCount < result.ReturnedCount
            || result.Summary.Mode is not ("search" or "exact"))
        {
            throw InvalidResult();
        }

        if (result.Summary.Mode == "exact" && (result.ReturnedCount != 1 || result.Summary.MatchedTypeCount != 1))
        {
            throw InvalidResult();
        }

        foreach (var type in result.Types)
        {
            ValidateType(type, result.Summary.Mode);
        }
    }

    private static void ValidateType(RevitApiMetadataType type, string mode)
    {
        if (type is null
            || string.IsNullOrEmpty(type.TypeName)
            || type.MatchedMembers is null
            || type.Properties is null
            || type.MatchedMembers.Count > RevitApiMetadataIndex.MaxMatchedMembers
            || type.MatchedMembersOmittedCount < 0
            || type.RejectedOmittedCount < 0
            || type.Properties.Count > RevitApiMetadataIndex.MaxListedProperties)
        {
            throw InvalidResult();
        }

        if (mode == "search" && (!type.IsElement || type.Properties.Count != 0 || type.RejectedOmittedCount != 0))
        {
            throw InvalidResult();
        }

        if (!type.IsElement && (type.Properties.Count != 0 || type.MatchedMembers.Count != 0))
        {
            throw InvalidResult();
        }

        foreach (var property in type.Properties)
        {
            if (property is null || string.IsNullOrEmpty(property.Name) || string.IsNullOrEmpty(property.ReturnType))
            {
                throw InvalidResult();
            }

            if (property.CanProject)
            {
                if (property.Rejection is not null)
                {
                    throw InvalidResult();
                }
            }
            else if (property.Rejection is null || !Rejections.Contains(property.Rejection))
            {
                throw InvalidResult();
            }
        }
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
