using System.Text.Json;
using System.Text.Json.Serialization;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal static class RunCSharpQueryErrorDetailsSanitizer
{
    private const int MaxDiagnostics = 8;

    private const int MaxDiagnosticMessageCharacters = 180;

    private const int MaxTokenCharacters = 64;

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    private static readonly HashSet<string> Reasons = new(StringComparer.Ordinal)
    {
        "no_active_document",
        "project_document_required",
        "document_mismatch",
        "document_modifiable"
    };

    private static readonly HashSet<string> CompileOutcomes = new(StringComparer.Ordinal)
    {
        "success",
        "policy_rejected",
        "compile_failed",
        "api_unsupported",
        "identity_rejected",
        "invalid_request"
    };

    public static JsonElement? Sanitize(JsonElement? details)
    {
        if (details is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        RunCSharpQueryErrorDetails? parsed;
        try
        {
            parsed = element.Deserialize<RunCSharpQueryErrorDetails>(ParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed is null)
        {
            return null;
        }

        var sanitized = new RunCSharpQueryErrorDetails
        {
            Reason = Reasons.Contains(parsed.Reason ?? "") ? parsed.Reason : null,
            SourceSha256 = ValidHash(parsed.SourceSha256),
            PolicyVersion = parsed.PolicyVersion == 1 ? 1 : null,
            RevitApi = ValidApi(parsed.RevitApi),
            Diagnostics = ValidDiagnostics(parsed.Diagnostics),
            CompileOutcome = CompileOutcomes.Contains(parsed.CompileOutcome ?? "") ? parsed.CompileOutcome : null
        };

        if (sanitized.Reason is null
            && sanitized.SourceSha256 is null
            && sanitized.PolicyVersion is null
            && sanitized.RevitApi is null
            && sanitized.Diagnostics is null
            && sanitized.CompileOutcome is null)
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(sanitized, McpJson.Options);
    }

    private static string? ValidHash(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return null;
        }

        foreach (var character in value)
        {
            var hex = character is >= '0' and <= '9' || character is >= 'a' and <= 'f';
            if (!hex)
            {
                return null;
            }
        }

        return value;
    }

    private static RunCSharpQueryApiIdentity? ValidApi(RunCSharpQueryApiIdentity? api)
    {
        if (api is null || !ValidToken(api.Name) || !ValidToken(api.Version))
        {
            return null;
        }

        return new RunCSharpQueryApiIdentity
        {
            Name = api.Name,
            Version = api.Version
        };
    }

    private static bool ValidToken(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxTokenCharacters)
        {
            return false;
        }

        foreach (var character in value)
        {
            var allowed = character is (>= 'A' and <= 'Z')
                or (>= 'a' and <= 'z')
                or (>= '0' and <= '9')
                or '.'
                or '_';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<RunCSharpQueryDiagnostic>? ValidDiagnostics(
        IReadOnlyList<RunCSharpQueryDiagnostic>? diagnostics)
    {
        if (diagnostics is null)
        {
            return null;
        }

        var accepted = new List<RunCSharpQueryDiagnostic>(Math.Min(diagnostics.Count, MaxDiagnostics));
        foreach (var diagnostic in diagnostics)
        {
            if (accepted.Count == MaxDiagnostics)
            {
                break;
            }

            if (diagnostic is null
                || !ValidToken(diagnostic.Code)
                || diagnostic.Line < 0
                || diagnostic.Column < 0
                || string.IsNullOrEmpty(diagnostic.Message)
                || diagnostic.Message.Length > MaxDiagnosticMessageCharacters
                || diagnostic.Message.Contains('\r')
                || diagnostic.Message.Contains('\n'))
            {
                continue;
            }

            accepted.Add(new RunCSharpQueryDiagnostic
            {
                Code = diagnostic.Code,
                Line = diagnostic.Line,
                Column = diagnostic.Column,
                Message = diagnostic.Message
            });
        }

        return accepted.Count == 0 ? null : accepted;
    }
}
