using System.Text.Json;

namespace BimIt.Revit.Compiler;

public sealed class CompilerRequest
{
    public int ContractVersion { get; init; }

    public int PolicyVersion { get; init; }

    public string Source { get; init; } = "";

    public string RevitApiPath { get; init; } = "";

    public RevitApiIdentity RevitApi { get; init; } = new("", "", "", 0);
}

public sealed class CompilerResponse
{
    public int ContractVersion { get; init; }

    public string Outcome { get; init; } = "";

    public int PolicyVersion { get; init; }

    public string SourceSha256 { get; init; } = "";

    public string CacheKey { get; init; } = "";

    public RevitApiIdentity? RevitApi { get; init; }

    public IReadOnlyList<CompilerDiagnostic> Diagnostics { get; init; } = [];

    public byte[]? Pe { get; init; }
}

public static class CompilerWire
{
    private static readonly string[] RequestProperties =
    [
        "contractVersion",
        "policyVersion",
        "source",
        "revitApiPath",
        "revitApi"
    ];

    private static readonly string[] IdentityProperties = ["name", "version", "publicKeyToken", "length"];

    private static readonly string[] DiagnosticProperties = ["code", "line", "column", "message"];

    public static byte[] SerializeRequest(CompilerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("contractVersion", request.ContractVersion);
            writer.WriteNumber("policyVersion", request.PolicyVersion);
            writer.WriteString("source", request.Source);
            writer.WriteString("revitApiPath", request.RevitApiPath);
            WriteIdentity(writer, "revitApi", request.RevitApi);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static byte[] SerializeResponse(CompilerResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("contractVersion", response.ContractVersion);
            writer.WriteString("outcome", response.Outcome);
            writer.WriteNumber("policyVersion", response.PolicyVersion);
            writer.WriteString("sourceSha256", response.SourceSha256);
            writer.WriteString("cacheKey", response.CacheKey);
            WriteIdentity(writer, "revitApi", response.RevitApi);
            writer.WritePropertyName("diagnostics");
            writer.WriteStartArray();
            foreach (var diagnostic in response.Diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("code", diagnostic.Code);
                writer.WriteNumber("line", diagnostic.Line);
                writer.WriteNumber("column", diagnostic.Column);
                writer.WriteString("message", diagnostic.Message);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (response.Outcome == CompilerOutcomes.Success)
            {
                writer.WriteBase64String("pe", response.Pe ?? []);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static bool TryReadRequest(ReadOnlySpan<byte> utf8, out CompilerRequest request)
    {
        request = new CompilerRequest();
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            if (!HasExactProperties(document.RootElement, RequestProperties))
            {
                return false;
            }

            if (!TryIdentity(document.RootElement.GetProperty("revitApi"), out var identity))
            {
                return false;
            }

            var source = document.RootElement.GetProperty("source").GetString();
            var path = document.RootElement.GetProperty("revitApiPath").GetString();
            if (source is null || path is null)
            {
                return false;
            }

            request = new CompilerRequest
            {
                ContractVersion = document.RootElement.GetProperty("contractVersion").GetInt32(),
                PolicyVersion = document.RootElement.GetProperty("policyVersion").GetInt32(),
                Source = source,
                RevitApiPath = path,
                RevitApi = identity
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool TryReadResponse(ReadOnlySpan<byte> utf8, out CompilerResponse response)
    {
        response = new CompilerResponse();
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (property.Name is "revitApiPath" or "source" or "stackTrace" or "exception")
                {
                    return false;
                }
            }

            if (!root.TryGetProperty("outcome", out var outcomeProperty))
            {
                return false;
            }

            var outcome = outcomeProperty.GetString() ?? "";
            var allowPe = outcome == CompilerOutcomes.Success;
            if (!HasResponseProperties(root, allowPe))
            {
                return false;
            }

            if (!TryIdentity(root.GetProperty("revitApi"), out var identity))
            {
                return false;
            }

            if (!TryDiagnostics(root.GetProperty("diagnostics"), out var diagnostics))
            {
                return false;
            }

            byte[]? pe = null;
            if (allowPe)
            {
                pe = root.GetProperty("pe").GetBytesFromBase64();
            }

            response = new CompilerResponse
            {
                ContractVersion = root.GetProperty("contractVersion").GetInt32(),
                Outcome = outcome,
                PolicyVersion = root.GetProperty("policyVersion").GetInt32(),
                SourceSha256 = root.GetProperty("sourceSha256").GetString() ?? "",
                CacheKey = root.GetProperty("cacheKey").GetString() ?? "",
                RevitApi = identity,
                Diagnostics = diagnostics,
                Pe = pe
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void WriteIdentity(Utf8JsonWriter writer, string name, RevitApiIdentity? identity)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("name", identity?.Name ?? "");
        writer.WriteString("version", identity?.Version ?? "");
        writer.WriteString("publicKeyToken", identity?.PublicKeyToken ?? "");
        writer.WriteNumber("length", identity?.Length ?? 0);
        writer.WriteEndObject();
    }

    private static bool TryIdentity(JsonElement element, out RevitApiIdentity identity)
    {
        identity = new RevitApiIdentity("", "", "", 0);
        if (!HasExactProperties(element, IdentityProperties))
        {
            return false;
        }

        var name = element.GetProperty("name").GetString();
        var version = element.GetProperty("version").GetString();
        var token = element.GetProperty("publicKeyToken").GetString();
        if (name is null || version is null || token is null)
        {
            return false;
        }

        identity = new RevitApiIdentity(name, version, token, element.GetProperty("length").GetInt64());
        return true;
    }

    private static bool TryDiagnostics(JsonElement element, out IReadOnlyList<CompilerDiagnostic> diagnostics)
    {
        diagnostics = [];
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > CompilerProtocol.MaxDiagnostics)
        {
            return false;
        }

        var found = new List<CompilerDiagnostic>();
        foreach (var item in element.EnumerateArray())
        {
            if (!HasExactProperties(item, DiagnosticProperties))
            {
                return false;
            }

            var code = item.GetProperty("code").GetString();
            var message = item.GetProperty("message").GetString();
            if (code is null || message is null || message.Length > CompilerProtocol.MaxDiagnosticMessageCharacters)
            {
                return false;
            }

            if (message.Contains('\n') || message.Contains('\r'))
            {
                return false;
            }

            found.Add(new CompilerDiagnostic(code, item.GetProperty("line").GetInt32(), item.GetProperty("column").GetInt32(), message));
        }

        diagnostics = found;
        return true;
    }

    private static bool HasResponseProperties(JsonElement element, bool allowPe)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "contractVersion",
            "outcome",
            "policyVersion",
            "sourceSha256",
            "cacheKey",
            "revitApi",
            "diagnostics"
        };
        if (allowPe)
        {
            expected.Add("pe");
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                return false;
            }
        }

        return actual.SetEquals(expected);
    }

    private static bool HasExactProperties(JsonElement element, IReadOnlyList<string> names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                return false;
            }
        }

        return actual.Count == names.Count && names.All(actual.Contains);
    }
}
