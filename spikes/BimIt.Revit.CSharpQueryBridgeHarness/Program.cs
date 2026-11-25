using System.Text.Json;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

const string levelProjection =
    "return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(level => new { element_ref = elementRef(level), name = level.Name, elevation = level.Elevation }).ToList();";
const string forbidden = "return Environment.TickCount;";

if (args.Length != 1 || args[0] is not ("A" or "B" or "C" or "D"))
{
    Console.Error.WriteLine("Usage: BimIt.Revit.CSharpQueryBridgeHarness <A|B|C|D>");
    return 2;
}

var mode = args[0];
var capabilityPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "RevitMCP",
    "capabilities.json");
if (mode == "A" && File.Exists(capabilityPath))
{
    Console.Error.WriteLine("Case A requires the capability file to be absent: " + capabilityPath);
    return 2;
}

var store = new FileRegistrationStore();
var session = await store.ReadSessionAsync(System.Diagnostics.Process.GetCurrentProcess().SessionId, CancellationToken.None);
var registration = session
    .Select(item => item.Registration)
    .Where(item => item is not null)
    .Cast<RevitInstanceRegistration>()
    .OrderByDescending(item => item.BridgeProtocolVersion)
    .FirstOrDefault();
if (registration is null)
{
    Console.Error.WriteLine("No Revit Bridge registration was found for this Windows session.");
    return 2;
}

await using var client = await NamedPipeBridgeClient.ConnectAsync(registration.PipeName, TimeSpan.FromSeconds(5), CancellationToken.None);
var handshake = await client.HandshakeAsync(new BridgeHandshakeRequest
{
    ExpectedInstanceId = registration.InstanceId,
    SupportedProtocolVersions = BridgeProtocol.SupportedVersions,
    ClientName = "BimIt.Revit.CSharpQueryBridgeHarness"
}, CancellationToken.None);
Console.WriteLine("negotiated=" + handshake.SelectedProtocolVersion);

var context = await client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(10), CancellationToken.None);
var documentId = context.Document?.DocumentId;
if (string.IsNullOrWhiteSpace(documentId))
{
    Console.Error.WriteLine("The active document has no document_id.");
    return 2;
}

var source = mode == "C" ? forbidden : levelProjection;
try
{
    var result = await client.RunCSharpQueryAsync(
        new RunCSharpQueryRequest { DocumentId = documentId, Source = source },
        TimeSpan.FromSeconds(40),
        CancellationToken.None);
    Console.WriteLine("status=success");
    Console.WriteLine("source_sha256=" + result.SourceSha256);
    Console.WriteLine("cache_key=" + result.CacheKey);
    Console.WriteLine("policy_version=" + result.PolicyVersion);
    Console.WriteLine("revit_api=" + result.RevitApi.Name + " " + result.RevitApi.Version);
    Console.WriteLine("compile_outcome=" + result.CompileOutcome);
    Console.WriteLine("duration_ms=" + result.DurationMs);
    Console.WriteLine("result_characters=" + result.ResultCharacters);
    Console.WriteLine(result.Result.GetRawText());
    return mode is "A" or "D" ? 1 : 0;
}
catch (BridgeException exception)
{
    Console.WriteLine("status=error");
    Console.WriteLine("code=" + exception.ErrorCode);
    if (exception.Details is JsonElement details)
    {
        Console.WriteLine(details.GetRawText());
    }

    var expectedDisabled = mode is "A" or "D" && exception.ErrorCode == "CSHARP_QUERY_DISABLED";
    var expectedPolicy = mode == "C" && IsPolicyRejection(exception);
    return expectedDisabled || expectedPolicy ? 0 : 1;
}

static bool IsPolicyRejection(BridgeException exception)
{
    if (exception.ErrorCode != "CSHARP_QUERY_POLICY_REJECTED" || exception.Details is not JsonElement details)
    {
        return false;
    }

    return details.TryGetProperty("compile_outcome", out var outcome)
        && outcome.GetString() == "policy_rejected"
        && details.TryGetProperty("diagnostics", out var diagnostics)
        && diagnostics.GetArrayLength() <= 8;
}
