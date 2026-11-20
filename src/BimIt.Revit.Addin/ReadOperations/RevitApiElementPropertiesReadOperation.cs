using System.Globalization;
using System.Text.Json;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// CAP-0017 <c>revit_api.element_properties</c> version 1.
/// Type and property checks run before collection. Numeric values have no physical unit.
/// </summary>
internal sealed class RevitApiElementPropertiesReadOperation : RevitReadOperation<RevitApiElementPropertiesRequest, RevitApiElementPropertiesResult>
{
    internal const string OperationId = "revit_api.element_properties";

    internal const int Version = 1;

    internal const string OperationTitle = "Read element properties";

    internal const string OperationDescription =
        "Reads scalar public instance properties of one concrete Element subclass. type_name and properties come from revit_api.metadata. Each row includes an opaque element_ref. Numeric values are raw Revit API numbers and have no physical unit. This operation does not write and does not accept source code.";

    internal const string InputSchemaJson =
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

    internal const string OutputSchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary", "returned_count", "elements"],
          "properties": {
            "summary": {
              "type": "object",
              "additionalProperties": false,
              "required": ["type_name", "matched_element_count"],
              "properties": {
                "type_name": { "type": "string", "minLength": 1 },
                "matched_element_count": { "type": "integer", "minimum": 0 }
              }
            },
            "returned_count": { "type": "integer", "minimum": 0 },
            "elements": {
              "type": "array",
              "maxItems": 200,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["element_ref", "values"],
                "properties": {
                  "element_ref": { "type": "string", "minLength": 1 },
                  "values": {
                    "type": "array",
                    "maxItems": 16,
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["name", "kind"],
                      "properties": {
                        "name": { "type": "string", "minLength": 1, "maxLength": 128 },
                        "kind": { "type": "string", "enum": ["string", "boolean", "integer", "number", "enum", "null"] },
                        "text": { "type": "string", "maxLength": 4096 },
                        "boolean": { "type": "boolean" },
                        "integer": { "type": "integer" },
                        "number": { "type": "number", "description": "Raw Revit API number. No physical unit." }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    public RevitApiElementPropertiesReadOperation()
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

    protected override void ValidateRequest(RevitApiElementPropertiesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TypeName))
        {
            throw InvalidInput("type_name is required.");
        }

        if (request.Properties is null || request.Properties.Length is < 1 or > 16)
        {
            throw InvalidInput("properties must contain 1 to 16 names.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in request.Properties)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 128 || name.Contains('.', StringComparison.Ordinal))
            {
                throw InvalidInput("properties must be ordinal property names.");
            }

            if (!seen.Add(name))
            {
                throw InvalidInput("properties contains a duplicate name.");
            }
        }
    }

    protected override byte[] CreateFingerprint(
        RevitApiElementPropertiesRequest request,
        string documentId,
        ReadOperationDescriptor descriptor)
    {
        var builder = new DeclarativeQueryFingerprintBuilder(descriptor.Id)
            .Value(descriptor.ContractVersion.ToString(CultureInfo.InvariantCulture))
            .Value(documentId)
            .Value(request.TypeName);
        if (request.Properties is not null)
        {
            foreach (var name in request.Properties)
            {
                builder.Value(name);
            }
        }

        return builder.Compute();
    }

    protected override ReadOperationExecutionResult ExecuteRead(
        RevitApiElementPropertiesRequest request,
        ReadOperationExecutionContext context)
    {
        var elementType = RevitApiTypeResolver.RequireForRead(request.TypeName);
        var properties = RevitApiPropertyPolicy.RequireProjectable(
            RevitApiTypeResolver.Assembly,
            elementType,
            request.Properties!);
        if (context.Host is not RevitActiveReadDocument active)
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The read operation could not be read.");
        }

        var matched = RevitApiElementReader.Collect(active.Document, elementType);
        RevitApiElementReader.ThrowIfBecameModifiable(active.Document);
        var page = RevitApiReadPages.Slice(matched, context.Offset, context.PageSize);
        var rows = new List<RevitApiElementRow>(page.Count);
        foreach (var element in page)
        {
            var values = new List<RevitApiScalar>(properties.Count);
            foreach (var property in properties)
            {
                try
                {
                    values.Add(RevitApiPropertyPolicy.Read(property, element));
                }
                catch (BridgeException)
                {
                    RevitApiElementReader.ThrowIfBecameModifiable(active.Document);
                    throw;
                }

                RevitApiElementReader.ThrowIfBecameModifiable(active.Document);
            }

            rows.Add(new RevitApiElementRow
            {
                ElementRef = RevitApiElementReader.ElementRef(element),
                Values = values
            });
        }

        RevitApiElementReader.ThrowIfBecameModifiable(active.Document);
        return RevitApiReadPages.Complete(
            context,
            matched.Count,
            rows,
            slice => new RevitApiElementPropertiesResult
            {
                Summary = new RevitApiElementPropertiesSummary
                {
                    TypeName = request.TypeName!,
                    MatchedElementCount = matched.Count
                },
                ReturnedCount = slice.Count,
                Elements = slice
            },
            CreateFingerprint(request, context.DocumentId, context.Descriptor));
    }

    protected override void ValidateTypedResult(RevitApiElementPropertiesResult result)
    {
        if (result.Summary is null
            || string.IsNullOrEmpty(result.Summary.TypeName)
            || result.Elements is null
            || result.ReturnedCount != result.Elements.Count
            || result.Summary.MatchedElementCount < result.ReturnedCount)
        {
            throw InvalidResult();
        }

        foreach (var row in result.Elements)
        {
            if (row is null || string.IsNullOrEmpty(row.ElementRef) || row.Values is null || row.Values.Count > 16)
            {
                throw InvalidResult();
            }

            foreach (var value in row.Values)
            {
                ValidateScalar(value);
            }
        }
    }

    private static void ValidateScalar(RevitApiScalar value)
    {
        if (value is null || string.IsNullOrEmpty(value.Name))
        {
            throw InvalidResult();
        }

        var payloads = (value.Text is not null ? 1 : 0)
            + (value.Boolean is not null ? 1 : 0)
            + (value.Integer is not null ? 1 : 0)
            + (value.Number is not null ? 1 : 0);
        var valid = value.Kind switch
        {
            "null" => payloads == 0,
            "string" or "enum" => payloads == 1 && value.Text is not null && value.Text.Length <= RevitApiPropertyPolicy.MaxStringLength,
            "boolean" => payloads == 1 && value.Boolean is not null,
            "integer" => payloads == 1
                && value.Integer is long integer
                && integer >= RevitApiPropertyPolicy.MinSafeInteger
                && integer <= RevitApiPropertyPolicy.MaxSafeInteger,
            "number" => payloads == 1 && value.Number is double number && double.IsFinite(number),
            _ => false
        };
        if (!valid)
        {
            throw InvalidResult();
        }
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
