using System.Globalization;
using System.Text.Json;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// CAP-0016 <c>project.user_worksets</c> version 1.
/// Not composed into the host. Revit access happens only through the execution context.
/// </summary>
internal sealed class ProjectUserWorksetsReadOperation : RevitReadOperation<UserWorksetsRequest, UserWorksetsResult>
{
    internal const string OperationId = "project.user_worksets";

    internal const int Version = 1;

    internal const string OperationTitle = "User worksets";

    internal const string OperationDescription =
        "User-created worksets of the active host project. Omits view, family, standard, and other internal worksets, and omits worksets that belong to linked documents. A project that is not workshared returns an empty inventory.";

    internal const string InputSchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "name_contains": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "OrdinalIgnoreCase substring of the workset name. Omitted matches every user workset."
            }
          }
        }
        """;

    internal const string OutputSchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary", "returned_count", "worksets"],
          "properties": {
            "summary": {
              "type": "object",
              "additionalProperties": false,
              "required": ["worksharing_enabled", "matched_workset_count"],
              "properties": {
                "worksharing_enabled": { "type": "boolean" },
                "matched_workset_count": { "type": "integer", "minimum": 0 }
              }
            },
            "returned_count": { "type": "integer", "minimum": 0 },
            "worksets": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["name", "is_default", "is_visible_by_default"],
                "properties": {
                  "name": { "type": "string" },
                  "is_default": { "type": "boolean" },
                  "is_visible_by_default": { "type": "boolean" }
                }
              }
            }
          }
        }
        """;

    public ProjectUserWorksetsReadOperation()
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

    internal static byte[] FingerprintFor(UserWorksetsRequest request, string documentId, ReadOperationDescriptor descriptor)
    {
        return new DeclarativeQueryFingerprintBuilder(descriptor.Id)
            .Value(descriptor.ContractVersion.ToString(CultureInfo.InvariantCulture))
            .Value(documentId)
            .Value(request.NameContains)
            .Compute();
    }

    protected override void ValidateRequest(UserWorksetsRequest request)
    {
        if (request.NameContains is not null && string.IsNullOrWhiteSpace(request.NameContains))
        {
            throw InvalidInput("name_contains is invalid.");
        }
    }

    protected override byte[] CreateFingerprint(UserWorksetsRequest request, string documentId, ReadOperationDescriptor descriptor)
    {
        return FingerprintFor(request, documentId, descriptor);
    }

    protected override ReadOperationExecutionResult ExecuteRead(UserWorksetsRequest request, ReadOperationExecutionContext context)
    {
        return ProjectUserWorksetsQuery.Complete(request, context);
    }

    protected override void ValidateTypedResult(UserWorksetsResult result)
    {
        if (result.Summary is null
            || result.Worksets is null
            || result.ReturnedCount != result.Worksets.Count
            || result.ReturnedCount < 0
            || result.Summary.MatchedWorksetCount < result.ReturnedCount)
        {
            throw InvalidResult();
        }

        foreach (var row in result.Worksets)
        {
            if (row is null || row.Name is null)
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
