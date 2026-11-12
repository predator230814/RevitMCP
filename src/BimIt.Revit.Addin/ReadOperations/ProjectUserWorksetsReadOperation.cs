using System.Text.Json;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// CAP-0016 <c>project.user_worksets</c> version 1 contract.
/// This definition is not composed and does not read Revit.
/// </summary>
internal sealed class ProjectUserWorksetsReadOperation : IRevitReadOperation
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

    public ReadOperationDescriptor Descriptor { get; }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
