using BimIt.Revit.Compiler;

namespace BimIt.Revit.CompilerHost;

public static class Program
{
    public static int Main()
    {
        try
        {
            var bytes = ReadBounded();
            if (bytes is null)
            {
                return Fail(Invalid("compiler request exceeds the size bound"));
            }

            if (!CompilerWire.TryReadRequest(bytes, out var request))
            {
                return Fail(Invalid("compiler request is malformed"));
            }

            var response = CSharpCompilationPipeline.Compile(request);
            var payload = CompilerWire.SerializeResponse(response);
            if (payload.Length > CompilerProtocol.MaxResponseBytes)
            {
                return Fail(Invalid("compiler response exceeds the size bound"));
            }

            Console.OpenStandardOutput().Write(payload, 0, payload.Length);
            return 0;
        }
        catch (Exception)
        {
            try
            {
                return Fail(Invalid("compiler host failed"));
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }

    private static byte[]? ReadBounded()
    {
        var input = Console.OpenStandardInput();
        var buffer = new byte[CompilerProtocol.MaxRequestBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = input.Read(buffer, read, buffer.Length - read);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        if (read > CompilerProtocol.MaxRequestBytes)
        {
            return null;
        }

        return buffer[..read];
    }

    private static CompilerResponse Invalid(string message)
    {
        return new CompilerResponse
        {
            ContractVersion = CompilerProtocol.ContractVersion,
            Outcome = CompilerOutcomes.InvalidRequest,
            PolicyVersion = CompilerProtocol.PolicyVersion,
            Diagnostics = [CompilerDiagnostic.Create(CompilerResultCodes.InvalidSource, 0, 0, message)]
        };
    }

    private static int Fail(CompilerResponse response)
    {
        var payload = CompilerWire.SerializeResponse(response);
        Console.OpenStandardOutput().Write(payload, 0, payload.Length);
        return 1;
    }
}
