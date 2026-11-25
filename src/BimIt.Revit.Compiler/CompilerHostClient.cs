using System.Diagnostics;

namespace BimIt.Revit.Compiler;

public sealed class CompilerCallResult
{
    private CompilerCallResult(bool accepted, string code, string message, CompilerResponse? response)
    {
        Accepted = accepted;
        Code = code;
        Message = message;
        Response = response;
    }

    public bool Accepted { get; }

    public string Code { get; }

    public string Message { get; }

    public CompilerResponse? Response { get; }

    public static CompilerCallResult Fail(string code, string message)
    {
        return new CompilerCallResult(false, code, message, null);
    }

    public static CompilerCallResult Ok(CompilerResponse response)
    {
        var code = response.Outcome switch
        {
            CompilerOutcomes.Success => "",
            CompilerOutcomes.PolicyRejected => CompilerResultCodes.PolicyRejected,
            CompilerOutcomes.CompileFailed => CompilerResultCodes.CompileFailed,
            CompilerOutcomes.ApiUnsupported => CompilerResultCodes.ApiUnsupported,
            CompilerOutcomes.InvalidRequest => CompilerResultCodes.InvalidSource,
            _ => CompilerResultCodes.CompilerFailed
        };
        return new CompilerCallResult(true, code, "", response);
    }
}

public static class CompilerHostClient
{
    public static CompilerCallResult Run(string executablePath, CompilerRequest request)
    {
        return Run(executablePath, request, CompilerProtocol.TimeoutMilliseconds);
    }

    public static CompilerCallResult Run(string executablePath, CompilerRequest request, int timeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeoutMilliseconds <= 0)
        {
            return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler host timed out");
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathRooted(executablePath))
        {
            return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler executable is missing");
        }

        if (!File.Exists(executablePath))
        {
            return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler executable is missing");
        }

        byte[] payload;
        try
        {
            payload = CompilerWire.SerializeRequest(request);
        }
        catch (Exception)
        {
            return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler launch failed");
        }

        if (payload.Length > CompilerProtocol.MaxRequestBytes)
        {
            return CompilerCallResult.Fail(CompilerResultCodes.InvalidSource, "compiler request exceeds the size bound");
        }

        Process? process = null;
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? ""
            };
            process = new Process { StartInfo = start, EnableRaisingEvents = true };
            if (!process.Start())
            {
                return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler launch failed");
            }

            var discardedErrors = 0;
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null && discardedErrors < 32)
                {
                    discardedErrors++;
                }
            };
            process.BeginErrorReadLine();
            var stdout = new MemoryStream();
            var read = Task.Run(() => ReadStdout(process, stdout));
            using (var input = process.StandardInput)
            {
                input.BaseStream.Write(payload, 0, payload.Length);
            }

            if (!process.WaitForExit(timeoutMilliseconds))
            {
                Terminate(process);
                _ = read.Wait(1_000);
                return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler host timed out");
            }

            if (!read.Wait(5_000))
            {
                Terminate(process);
                return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler host failed");
            }

            process.WaitForExit();
            var readResult = read.Result;
            if (readResult < 0)
            {
                var message = readResult == -1
                    ? "compiler response exceeds the size bound"
                    : "compiler host failed";
                return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, message);
            }

            if (process.ExitCode != 0)
            {
                return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler host failed");
            }

            if (!CompilerWire.TryReadResponse(stdout.ToArray(), out var response))
            {
                return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler response is malformed");
            }

            return Validate(request, response);
        }
        catch (Exception)
        {
            if (process is not null)
            {
                Terminate(process);
            }

            return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler launch failed");
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static int ReadStdout(Process process, MemoryStream stdout)
    {
        var buffer = new byte[8_192];
        try
        {
            while (true)
            {
                var count = process.StandardOutput.BaseStream.Read(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    return 0;
                }

                if (stdout.Length + count > CompilerProtocol.MaxResponseBytes)
                {
                    Terminate(process);
                    return -1;
                }

                stdout.Write(buffer, 0, count);
            }
        }
        catch (Exception)
        {
            return -2;
        }
    }

    private static CompilerCallResult Validate(CompilerRequest request, CompilerResponse response)
    {
        if (response.ContractVersion != request.ContractVersion
            || response.PolicyVersion != request.PolicyVersion
            || response.RevitApi is null
            || !response.RevitApi.Matches(request.RevitApi))
        {
            return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler response metadata does not match the request");
        }

        var sourceHash = CompilerHashes.SourceSha256(request.Source);
        var cacheKey = CompilerHashes.CacheKey(request.Source, request.RevitApi, request.PolicyVersion);
        if (!string.Equals(response.SourceSha256, sourceHash, StringComparison.Ordinal)
            || !string.Equals(response.CacheKey, cacheKey, StringComparison.Ordinal))
        {
            return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler response metadata does not match the request");
        }

        if (Leaks(request, response))
        {
            return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler response is malformed");
        }

        if (response.Outcome == CompilerOutcomes.Success)
        {
            if (response.Pe is null || response.Pe.Length < 64 || response.Pe[0] != (byte)'M' || response.Pe[1] != (byte)'Z')
            {
                return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler response is malformed");
            }

            return CompilerCallResult.Ok(response);
        }

        if (response.Pe is { Length: > 0 })
        {
            return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler response is malformed");
        }

        if (response.Outcome is CompilerOutcomes.PolicyRejected
            or CompilerOutcomes.CompileFailed
            or CompilerOutcomes.ApiUnsupported
            or CompilerOutcomes.InvalidRequest
            or CompilerOutcomes.IdentityRejected)
        {
            return CompilerCallResult.Ok(response);
        }

        return CompilerCallResult.Fail(CompilerResultCodes.CompilerFailed, "compiler response is malformed");
    }

    private static bool Leaks(CompilerRequest request, CompilerResponse response)
    {
        if (request.RevitApiPath.Length > 0 && Contains(response, request.RevitApiPath))
        {
            return true;
        }

        return request.Source.Length >= 24 && Contains(response, request.Source);
    }

    private static bool Contains(CompilerResponse response, string value)
    {
        if (response.SourceSha256.Contains(value, StringComparison.Ordinal)
            || response.CacheKey.Contains(value, StringComparison.Ordinal)
            || response.Outcome.Contains(value, StringComparison.Ordinal))
        {
            return true;
        }

        return response.Diagnostics.Any(diagnostic =>
            diagnostic.Code.Contains(value, StringComparison.Ordinal)
            || diagnostic.Message.Contains(value, StringComparison.Ordinal));
    }

    private static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // The child already left. The caller still fails closed.
        }
    }
}
