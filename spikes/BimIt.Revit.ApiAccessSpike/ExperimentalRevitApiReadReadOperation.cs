using System.Globalization;
using System.Text.Json;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal sealed class ExperimentalRevitApiReadReadOperation : RevitReadOperation<ApiElementReadRequest, ApiElementReadResult>
{
    internal const string OperationId = "experimental.revit_api_read";

    internal const int Version = 1;

    private const string InputSchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["type_name", "properties"],
          "properties": {
            "type_name": { "type": "string", "minLength": 1, "maxLength": 256 },
            "properties": {
              "type": "array",
              "minItems": 1,
              "maxItems": 16,
              "uniqueItems": true,
              "items": { "type": "string", "minLength": 1, "maxLength": 128 }
            }
          }
        }
        """;

    private const string OutputSchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary", "returned_count", "rows"],
          "properties": {
            "summary": {
              "type": "object",
              "additionalProperties": false,
              "required": ["type_name", "matched_element_count"],
              "properties": {
                "type_name": { "type": "string" },
                "matched_element_count": { "type": "integer", "minimum": 0 }
              }
            },
            "returned_count": { "type": "integer", "minimum": 0 },
            "rows": {
              "type": "array",
              "maxItems": 200,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["values"],
                "properties": {
                  "values": {
                    "type": "array",
                    "maxItems": 16,
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["name", "kind"],
                      "properties": {
                        "name": { "type": "string" },
                        "kind": { "type": "string", "enum": ["string", "boolean", "integer", "number", "enum", "null"] },
                        "text": { "type": "string" },
                        "boolean": { "type": "boolean" },
                        "integer": { "type": "integer" },
                        "number": { "type": "number" }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    public ExperimentalRevitApiReadReadOperation()
    {
        Descriptor = new ReadOperationDescriptor
        {
            Id = OperationId,
            ContractVersion = Version,
            Title = "Read element properties",
            Description = "Reads instances of one concrete Element subclass from the Revit API assembly already loaded by this Revit process. type_name and properties come from the metadata operation. Only requested public instance getters are invoked. Methods, setters, indexers, and properties whose declared type is not a JSON scalar are rejected. This operation does not write and does not accept source code.",
            Context = "document",
            Effects = "read",
            Pagination = "cursor",
            InputSchema = Parse(InputSchemaJson),
            OutputSchema = Parse(OutputSchemaJson)
        };
    }

    public override ReadOperationDescriptor Descriptor { get; }

    internal static byte[] FingerprintFor(ApiElementReadRequest request, string documentId, ReadOperationDescriptor descriptor)
    {
        var builder = new DeclarativeQueryFingerprintBuilder(descriptor.Id)
            .Value(descriptor.ContractVersion.ToString(CultureInfo.InvariantCulture))
            .Value(documentId)
            .Value(request.TypeName);
        foreach (var property in request.Properties ?? [])
        {
            builder.Value(property);
        }

        return builder.Compute();
    }

    protected override void ValidateRequest(ApiElementReadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TypeName) || request.Properties is null || request.Properties.Length is < 1 or > 16)
        {
            throw InvalidInput("type_name and properties are required.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in request.Properties)
        {
            if (string.IsNullOrWhiteSpace(property) || property.Length > 128 || !seen.Add(property))
            {
                throw InvalidInput("properties contains an invalid name.");
            }
        }
    }

    protected override byte[] CreateFingerprint(ApiElementReadRequest request, string documentId, ReadOperationDescriptor descriptor)
    {
        return FingerprintFor(request, documentId, descriptor);
    }

    protected override ReadOperationExecutionResult ExecuteRead(ApiElementReadRequest request, ReadOperationExecutionContext context)
    {
        if (context.Host is not RevitActiveReadDocument active)
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The read operation could not be read.");
        }

        var resolved = ScalarPropertyProjection.RequireProjectable(
            TrustedRevitApiTypes.RequireConcreteElement(request.TypeName),
            request.Properties ?? []);
        var read = DocumentModificationGuard.Read(active.Document, () => ProjectPage(active.Document, resolved, context.Offset, context.PageSize));
        return ApiAccessSpikePages.Complete(
            context,
            read.MatchedCount,
            read.Rows.Cast<object>().ToArray(),
            rows => Shape(resolved.Type.FullName ?? request.TypeName!, read.MatchedCount, rows.Cast<ApiElementRow>().ToArray()),
            FingerprintFor(request, context.DocumentId, context.Descriptor));
    }

    protected override void ValidateTypedResult(ApiElementReadResult result)
    {
        if (result.Summary is null
            || result.Rows is null
            || string.IsNullOrEmpty(result.Summary.TypeName)
            || result.ReturnedCount != result.Rows.Count
            || result.Summary.MatchedElementCount < result.ReturnedCount)
        {
            throw InvalidResult();
        }

        foreach (var row in result.Rows)
        {
            if (row?.Values is null || row.Values.Count is < 1 or > 16)
            {
                throw InvalidResult();
            }

            foreach (var value in row.Values)
            {
                if (value is null || string.IsNullOrEmpty(value.Name) || !PayloadMatchesKind(value))
                {
                    throw InvalidResult();
                }
            }
        }
    }

    private static (int MatchedCount, IReadOnlyList<ApiElementRow> Rows) ProjectPage(
        Autodesk.Revit.DB.Document document,
        ResolvedElementProperties resolved,
        int offset,
        int pageSize)
    {
        var elements = GenericElementCollector.Collect(document, resolved.Type);
        var page = ApiAccessSpikePages.Slice(elements, offset, pageSize);
        var rows = new ApiElementRow[page.Count];
        for (var rowIndex = 0; rowIndex < page.Count; rowIndex++)
        {
            var values = new ApiScalarValue[resolved.Properties.Count];
            for (var propertyIndex = 0; propertyIndex < resolved.Properties.Count; propertyIndex++)
            {
                values[propertyIndex] = ScalarPropertyProjection.Read(resolved.Properties[propertyIndex], page[rowIndex]);
            }

            rows[rowIndex] = new ApiElementRow { Values = values };
        }

        return (elements.Count, rows);
    }

    private static ApiElementReadResult Shape(string typeName, int matchedCount, IReadOnlyList<ApiElementRow> rows)
    {
        return new ApiElementReadResult
        {
            Summary = new ApiReadSummary
            {
                TypeName = typeName,
                MatchedElementCount = matchedCount
            },
            ReturnedCount = rows.Count,
            Rows = rows
        };
    }

    private static bool PayloadMatchesKind(ApiScalarValue value)
    {
        var present = 0;
        if (value.Text is not null)
        {
            present++;
        }

        if (value.Boolean is not null)
        {
            present++;
        }

        if (value.Integer is not null)
        {
            present++;
        }

        if (value.Number is not null)
        {
            present++;
        }

        return value.Kind switch
        {
            "null" => present == 0,
            "string" or "enum" => value.Text is not null && present == 1,
            "boolean" => value.Boolean is not null && present == 1,
            "integer" => value.Integer is not null && present == 1,
            "number" => value.Number is not null && present == 1,
            _ => false
        };
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
