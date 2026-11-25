using BimIt.Revit.Compiler;

var directory = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
var modePath = Path.Combine(directory, "compiler-host-mode.txt");
var mode = File.Exists(modePath)
    ? File.ReadAllText(modePath).Trim()
    : Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "";

switch (mode)
{
    case "timeout":
        Thread.Sleep(TimeSpan.FromSeconds(15));
        return 0;
    case "nonzero":
        Write(Success(ReadRequest(), corrupt: false));
        return 4;
    case "malformed":
        Console.OpenStandardOutput().WriteByte((byte)'{');
        return 0;
    case "oversize":
        Console.OpenStandardOutput().Write(new byte[300_000]);
        return 0;
    case "once":
        File.AppendAllText(Path.Combine(directory, "requests.log"), "one\n");
        Write(Success(ReadRequest(), corrupt: false));
        return 0;
    case "mismatch":
        Write(Success(ReadRequest(), corrupt: true));
        return 0;
    default:
        Write(Success(ReadRequest(), corrupt: false));
        return 0;
}

static CompilerRequest ReadRequest()
{
    using var stream = Console.OpenStandardInput();
    using var copy = new MemoryStream();
    stream.CopyTo(copy);
    if (!CompilerWire.TryReadRequest(copy.ToArray(), out var request))
    {
        return new CompilerRequest();
    }

    return request;
}

static CompilerResponse Success(CompilerRequest request, bool corrupt)
{
    var pe = new byte[64];
    pe[0] = (byte)'M';
    pe[1] = (byte)'Z';
    return new CompilerResponse
    {
        ContractVersion = request.ContractVersion == 0 ? CompilerProtocol.ContractVersion : request.ContractVersion,
        Outcome = CompilerOutcomes.Success,
        PolicyVersion = request.PolicyVersion == 0 ? CompilerProtocol.PolicyVersion : request.PolicyVersion,
        SourceSha256 = CompilerHashes.SourceSha256(request.Source),
        CacheKey = corrupt ? "00" : CompilerHashes.CacheKey(request.Source, request.RevitApi, request.PolicyVersion),
        RevitApi = request.RevitApi,
        Diagnostics = [],
        Pe = pe
    };
}

static void Write(CompilerResponse response)
{
    var payload = CompilerWire.SerializeResponse(response);
    Console.OpenStandardOutput().Write(payload, 0, payload.Length);
}
