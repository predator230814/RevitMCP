using System.Text;
using System.Text.Json;

namespace BimIt.Revit.Assistant;

internal static class AssistantTextReader
{
    public static AssistantDocumentReadResult ReadPlain(string path, string displayName, long size, AssistantDocumentKind kind)
    {
        if (!TryReadBounded(path, out var text, out var truncated, out var failure))
        {
            return failure!;
        }

        return AssistantDocumentReadResult.Success(new AssistantDocumentArtifact(
            displayName,
            kind,
            size,
            text,
            truncated,
            [],
            false,
            null));
    }

    public static AssistantDocumentReadResult ReadJson(string path, string displayName, long size)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception)
        {
            return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.ReadFailed, AssistantDocumentErrors.ReadFailedMessage);
        }

        if (IsUtf16Bom(bytes) || bytes.AsSpan().Contains((byte)0))
        {
            return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
        }

        var span = bytes.AsSpan();
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(span);
        }
        catch (DecoderFallbackException)
        {
            return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
        }

        string normalized;
        try
        {
            using var document = JsonDocument.Parse(text);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                document.WriteTo(writer);
            }

            normalized = Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (JsonException)
        {
            return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
        }

        var truncated = normalized.Length > AssistantDocumentLimits.MaxTextCharacters;
        if (truncated)
        {
            normalized = normalized[..AssistantDocumentLimits.MaxTextCharacters];
        }

        return AssistantDocumentReadResult.Success(new AssistantDocumentArtifact(
            displayName,
            AssistantDocumentKind.Json,
            size,
            normalized,
            truncated,
            [],
            false,
            null));
    }

    private static bool TryReadBounded(string path, out string text, out bool truncated, out AssistantDocumentReadResult? failure)
    {
        text = "";
        truncated = false;
        failure = null;
        try
        {
            using var stream = AssistantDocumentReader.OpenRead(path);
            var probe = new byte[4];
            var probed = stream.Read(probe, 0, probe.Length);
            stream.Position = 0;
            if (IsUtf16Bom(probe.AsSpan(0, probed)))
            {
                failure = AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
                return false;
            }

            var maxBytes = (long)(AssistantDocumentLimits.MaxTextCharacters + 1) * 4;
            var toRead = (int)Math.Min(stream.Length, maxBytes);
            var buffer = new byte[toRead];
            var read = stream.Read(buffer, 0, toRead);
            if (buffer.AsSpan(0, read).Contains((byte)0))
            {
                failure = AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
                return false;
            }

            try
            {
                text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(buffer, 0, read);
            }
            catch (DecoderFallbackException)
            {
                if (stream.Length > read)
                {
                    text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(buffer, 0, read);
                }
                else
                {
                    failure = AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
                    return false;
                }
            }

            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text[1..];
            }

            text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            truncated = stream.Length > read || text.Length > AssistantDocumentLimits.MaxTextCharacters;
            if (text.Length > AssistantDocumentLimits.MaxTextCharacters)
            {
                text = text[..AssistantDocumentLimits.MaxTextCharacters];
            }

            return true;
        }
        catch (Exception)
        {
            failure = AssistantDocumentReadResult.Failure(AssistantDocumentErrors.ReadFailed, AssistantDocumentErrors.ReadFailedMessage);
            return false;
        }
    }

    private static bool IsUtf16Bom(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= 2
            && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));
    }
}
