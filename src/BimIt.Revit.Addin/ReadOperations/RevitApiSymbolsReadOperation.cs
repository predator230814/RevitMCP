using System.Globalization;
using System.Text.Json;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// CAP-0019 <c>revit_api.symbols</c> version 1.
/// Describes public types in the loaded Revit API assembly. It does not accept source and does not invoke members.
/// </summary>
internal sealed class RevitApiSymbolsReadOperation : RevitReadOperation<RevitApiSymbolRequest, RevitApiSymbolResult>
{
    internal const string OperationId = "revit_api.symbols";

    internal const int Version = 1;

    internal const string OperationTitle = "Revit API symbols";

    internal const string OperationDescription =
        "Describes public types and public members of the Revit API assembly already loaded by this Revit process. Pass query or type_name, not both. member is allowed only with type_name. Search returns type summaries. An exact type_name returns bounded C# signatures for that type. This operation does not accept source, does not read model elements, and does not invoke members.";

    internal const string InputSchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "query": { "type": "string", "minLength": 2, "maxLength": 128 },
            "type_name": { "type": "string", "minLength": 1, "maxLength": 256 },
            "member": { "type": "string", "minLength": 1, "maxLength": 128 }
          }
        }
        """;

    private static readonly string OutputSchemaJson = BuildOutputSchema();

    private static readonly HashSet<string> TypeKinds = new(StringComparer.Ordinal)
    {
        "class",
        "abstract class",
        "static class",
        "struct",
        "interface",
        "enum",
        "delegate"
    };

    private static readonly HashSet<string> MemberKinds = new(StringComparer.Ordinal)
    {
        "constructor",
        "method",
        "property",
        "field"
    };

    public RevitApiSymbolsReadOperation()
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

    protected override void ValidateRequest(RevitApiSymbolRequest request)
    {
        var hasQuery = request.Query is not null;
        var hasTypeName = request.TypeName is not null;
        if (hasQuery == hasTypeName)
        {
            throw InvalidInput("Pass query or type_name, not both.");
        }

        if (request.Member is not null && !hasTypeName)
        {
            throw InvalidInput("member requires type_name.");
        }

        if (request.Query is not null && request.Query.Length is < 2 or > 128)
        {
            throw InvalidInput("query must be between 2 and 128 characters.");
        }

        if (request.TypeName is not null && (request.TypeName.Length is < 1 or > 256 || string.IsNullOrWhiteSpace(request.TypeName)))
        {
            throw InvalidInput("type_name must be between 1 and 256 characters.");
        }

        if (request.Member is not null && request.Member.Length is < 1 or > 128)
        {
            throw InvalidInput("member must be between 1 and 128 characters.");
        }
    }

    protected override byte[] CreateFingerprint(RevitApiSymbolRequest request, string documentId, ReadOperationDescriptor descriptor)
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
            builder.Value("exact").Value(request.TypeName).Value(request.Member ?? string.Empty);
        }

        return builder.Compute();
    }

    protected override ReadOperationExecutionResult ExecuteRead(RevitApiSymbolRequest request, ReadOperationExecutionContext context)
    {
        var fingerprint = CreateFingerprint(request, context.DocumentId, context.Descriptor);
        if (request.TypeName is not null)
        {
            var described = RevitApiSymbolIndex.DescribeExact(request.TypeName, request.Member);
            return RevitApiReadPages.Complete(
                context,
                1,
                new[] { described },
                slice => new RevitApiSymbolResult
                {
                    Summary = new RevitApiSymbolSummary { Mode = "exact", MatchedTypeCount = 1 },
                    ReturnedCount = slice.Count,
                    Types = slice
                },
                fingerprint);
        }

        var matches = RevitApiSymbolIndex.Search(request.Query!);
        var page = RevitApiReadPages.Slice(matches, context.Offset, context.PageSize);
        return RevitApiReadPages.Complete(
            context,
            matches.Count,
            page,
            slice => new RevitApiSymbolResult
            {
                Summary = new RevitApiSymbolSummary { Mode = "search", MatchedTypeCount = matches.Count },
                ReturnedCount = slice.Count,
                Types = slice
            },
            fingerprint);
    }

    protected override void ValidateTypedResult(RevitApiSymbolResult result)
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

        if (result.Types.Count > RevitApiSymbolIndex.MaxSearchMatches)
        {
            throw InvalidResult();
        }

        foreach (var type in result.Types)
        {
            ValidateType(type, result.Summary.Mode);
        }
    }

    private static void ValidateType(RevitApiSymbolType type, string mode)
    {
        if (type is null
            || string.IsNullOrEmpty(type.FullName)
            || string.IsNullOrEmpty(type.SimpleName)
            || type.Namespace is null
            || !TypeKinds.Contains(type.Kind)
            || string.IsNullOrEmpty(type.AssemblyName)
            || string.IsNullOrEmpty(type.AssemblyVersion)
            || type.MatchedMembers is null
            || type.Constructors is null
            || type.Properties is null
            || type.Fields is null
            || type.Methods is null
            || type.Factories is null)
        {
            throw InvalidResult();
        }

        ValidateBound(type.MatchedMembers.Count, type.MatchedMembersOmittedCount, RevitApiSymbolIndex.MaxMatchedMembers);
        ValidateBound(type.Constructors.Count, type.ConstructorsOmittedCount, RevitApiSymbolIndex.MaxConstructors);
        ValidateBound(type.Properties.Count, type.PropertiesOmittedCount, RevitApiSymbolIndex.MaxProperties);
        ValidateBound(type.Fields.Count, type.FieldsOmittedCount, RevitApiSymbolIndex.MaxFields);
        ValidateBound(type.Methods.Count, type.MethodsOmittedCount, RevitApiSymbolIndex.MaxMethodGroups);
        ValidateBound(type.Factories.Count, type.FactoriesOmittedCount, RevitApiSymbolIndex.MaxFactories);
        ValidateObsolete(type.Obsolete, type.ObsoleteMessage, type.ObsoleteMessageTruncated);

        foreach (var name in type.MatchedMembers)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw InvalidResult();
            }
        }

        foreach (var signature in type.Constructors.Concat(type.Properties).Concat(type.Fields).Concat(type.Factories))
        {
            ValidateSignature(signature);
        }

        foreach (var group in type.Methods)
        {
            ValidateGroup(group, RevitApiSymbolIndex.MaxOverloadsPerGroup);
        }

        var memberMode = type.Member is not null;
        if (mode == "search" && (memberMode || !DetailsEmpty(type) || type.MatchedMembersOmittedCount < 0))
        {
            throw InvalidResult();
        }

        if (mode == "exact" && !memberMode && (type.MatchedMembers.Count != 0 || type.MatchedMembersOmittedCount != 0))
        {
            throw InvalidResult();
        }

        if (memberMode)
        {
            if (mode != "exact" || !DetailsEmpty(type) || type.MatchedMembers.Count != 0)
            {
                throw InvalidResult();
            }

            ValidateMember(type.Member!);
        }
    }

    private static bool DetailsEmpty(RevitApiSymbolType type)
    {
        return type.Constructors.Count == 0
            && type.ConstructorsOmittedCount == 0
            && type.Properties.Count == 0
            && type.PropertiesOmittedCount == 0
            && type.Fields.Count == 0
            && type.FieldsOmittedCount == 0
            && type.Methods.Count == 0
            && type.MethodsOmittedCount == 0
            && type.Factories.Count == 0
            && type.FactoriesOmittedCount == 0;
    }

    private static void ValidateMember(RevitApiSymbolMember member)
    {
        if (string.IsNullOrEmpty(member.Name)
            || !MemberKinds.Contains(member.Kind)
            || member.Signatures is null
            || member.Signatures.Count == 0)
        {
            throw InvalidResult();
        }

        ValidateBound(member.Signatures.Count, member.SignaturesOmittedCount, RevitApiSymbolIndex.MaxMemberOverloads);
        foreach (var signature in member.Signatures)
        {
            ValidateSignature(signature);
        }
    }

    private static void ValidateGroup(RevitApiSymbolGroup group, int cap)
    {
        if (group is null
            || string.IsNullOrEmpty(group.Name)
            || group.Signatures is null
            || group.Signatures.Count == 0)
        {
            throw InvalidResult();
        }

        ValidateBound(group.Signatures.Count, group.SignaturesOmittedCount, cap);
        foreach (var signature in group.Signatures)
        {
            ValidateSignature(signature);
        }
    }

    private static void ValidateSignature(RevitApiSymbolSignature signature)
    {
        if (signature is null
            || string.IsNullOrEmpty(signature.Text)
            || signature.Text.Length > RevitApiSymbolIndex.MaxSignatureLength
            || (signature.TextTruncated && signature.Text.Length != RevitApiSymbolIndex.MaxSignatureLength))
        {
            throw InvalidResult();
        }

        ValidateObsolete(signature.Obsolete, signature.ObsoleteMessage, signature.ObsoleteMessageTruncated);
    }

    private static void ValidateObsolete(bool obsolete, string? message, bool truncated)
    {
        if (message is not null && (message.Length == 0 || message.Length > RevitApiSymbolIndex.MaxObsoleteMessage))
        {
            throw InvalidResult();
        }

        if (truncated && (message is null || message.Length != RevitApiSymbolIndex.MaxObsoleteMessage))
        {
            throw InvalidResult();
        }

        if (!obsolete && (message is not null || truncated))
        {
            throw InvalidResult();
        }
    }

    private static void ValidateBound(int count, int omitted, int maximum)
    {
        if (count < 0 || omitted < 0 || count > maximum || (omitted > 0 && count != maximum))
        {
            throw InvalidResult();
        }
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string BuildOutputSchema()
    {
        var signature = SignatureSchema();
        return """
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
                  "maxItems": 
            """
            + RevitApiSymbolIndex.MaxSearchMatches.ToString(CultureInfo.InvariantCulture)
            + """
            ,
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": [
                      "full_name", "simple_name", "namespace", "kind", "assembly_name", "assembly_version",
                      "obsolete", "obsolete_message_truncated", "matched_members", "matched_members_omitted_count",
                      "constructors", "constructors_omitted_count", "properties", "properties_omitted_count",
                      "fields", "fields_omitted_count", "methods", "methods_omitted_count",
                      "factories", "factories_omitted_count"
                    ],
                    "properties": {
                      "full_name": { "type": "string", "minLength": 1 },
                      "simple_name": { "type": "string", "minLength": 1 },
                      "namespace": { "type": "string" },
                      "kind": { "type": "string", "enum": ["class", "abstract class", "static class", "struct", "interface", "enum", "delegate"] },
                      "base_type_name": { "type": "string", "minLength": 1 },
                      "assembly_name": { "type": "string", "minLength": 1 },
                      "assembly_version": { "type": "string", "minLength": 1 },
                      "obsolete": { "type": "boolean" },
                      "obsolete_message": { "type": "string", "minLength": 1, "maxLength": 
            """
            + RevitApiSymbolIndex.MaxObsoleteMessage.ToString(CultureInfo.InvariantCulture)
            + """
             },
                      "obsolete_message_truncated": { "type": "boolean" },
                      "matched_members": 
            """
            + StringArray(RevitApiSymbolIndex.MaxMatchedMembers)
            + """
            ,
                      "matched_members_omitted_count": { "type": "integer", "minimum": 0 },
                      "constructors": 
            """
            + SignatureArray(RevitApiSymbolIndex.MaxConstructors, signature, requireOne: false)
            + """
            ,
                      "constructors_omitted_count": { "type": "integer", "minimum": 0 },
                      "properties": 
            """
            + SignatureArray(RevitApiSymbolIndex.MaxProperties, signature, requireOne: false)
            + """
            ,
                      "properties_omitted_count": { "type": "integer", "minimum": 0 },
                      "fields": 
            """
            + SignatureArray(RevitApiSymbolIndex.MaxFields, signature, requireOne: false)
            + """
            ,
                      "fields_omitted_count": { "type": "integer", "minimum": 0 },
                      "methods": {
                        "type": "array",
                        "maxItems": 
            """
            + RevitApiSymbolIndex.MaxMethodGroups.ToString(CultureInfo.InvariantCulture)
            + """
            ,
                        "items": {
                          "type": "object",
                          "additionalProperties": false,
                          "required": ["name", "is_static", "signatures", "signatures_omitted_count"],
                          "properties": {
                            "name": { "type": "string", "minLength": 1 },
                            "is_static": { "type": "boolean" },
                            "signatures": 
            """
            + SignatureArray(RevitApiSymbolIndex.MaxOverloadsPerGroup, signature, requireOne: true)
            + """
            ,
                            "signatures_omitted_count": { "type": "integer", "minimum": 0 }
                          }
                        }
                      },
                      "methods_omitted_count": { "type": "integer", "minimum": 0 },
                      "factories": 
            """
            + SignatureArray(RevitApiSymbolIndex.MaxFactories, signature, requireOne: false)
            + """
            ,
                      "factories_omitted_count": { "type": "integer", "minimum": 0 },
                      "member": {
                        "type": "object",
                        "additionalProperties": false,
                        "required": ["name", "kind", "is_static", "signatures", "signatures_omitted_count"],
                        "properties": {
                          "name": { "type": "string", "minLength": 1, "maxLength": 128 },
                          "kind": { "type": "string", "enum": ["constructor", "method", "property", "field"] },
                          "is_static": { "type": "boolean" },
                          "signatures": 
            """
            + SignatureArray(RevitApiSymbolIndex.MaxMemberOverloads, signature, requireOne: true)
            + """
            ,
                          "signatures_omitted_count": { "type": "integer", "minimum": 0 }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;
    }

    private static string SignatureSchema()
    {
        return """
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["text", "obsolete", "obsolete_message_truncated", "text_truncated"],
              "properties": {
                "text": { "type": "string", "minLength": 1, "maxLength": 
            """
            + RevitApiSymbolIndex.MaxSignatureLength.ToString(CultureInfo.InvariantCulture)
            + """
             },
                "obsolete": { "type": "boolean" },
                "obsolete_message": { "type": "string", "minLength": 1, "maxLength": 
            """
            + RevitApiSymbolIndex.MaxObsoleteMessage.ToString(CultureInfo.InvariantCulture)
            + """
             },
                "obsolete_message_truncated": { "type": "boolean" },
                "text_truncated": { "type": "boolean" }
              }
            }
            """;
    }

    private static string SignatureArray(int maximum, string signature, bool requireOne)
    {
        var minimum = requireOne ? ", \"minItems\": 1" : string.Empty;
        return "{\"type\":\"array\",\"maxItems\":"
            + maximum.ToString(CultureInfo.InvariantCulture)
            + minimum
            + ",\"items\":"
            + signature
            + "}";
    }

    private static string StringArray(int maximum)
    {
        return "{\"type\":\"array\",\"maxItems\":"
            + maximum.ToString(CultureInfo.InvariantCulture)
            + ",\"items\":{\"type\":\"string\",\"minLength\":1}}";
    }
}
