using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

namespace BimIt.Revit.Assistant.Ipc;

public static class AssistantPipeNames
{
    public static string Create(int processId, string nonce)
    {
        var token = new string((nonce ?? "").Where(char.IsLetterOrDigit).ToArray());
        if (token.Length == 0)
        {
            token = Guid.NewGuid().ToString("N");
        }

        return $"bimit.revit.assistant.v1.p{processId}.{token}";
    }
}

public static class AssistantPipeFrames
{
    public static byte[] Encode(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length is 0 or > AssistantIpc.MaxMessageBytes)
        {
            throw new InvalidOperationException("Assistant IPC payload is outside the allowed size.");
        }

        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    public static async Task WriteAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        var frame = Encode(json);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        if (!await ReadExactAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is <= 0 or > AssistantIpc.MaxMessageBytes)
        {
            throw new InvalidOperationException("Assistant IPC frame length is outside the allowed size.");
        }

        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Encoding.UTF8.GetString(payload);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset == 0 && buffer.Length > 0 ? false : throw new InvalidOperationException("Assistant IPC frame ended early.");
            }

            offset += read;
        }

        return true;
    }
}

public static class AssistantPipeStreams
{
    public static NamedPipeServerStream CreateServer(string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    public static NamedPipeClientStream CreateClient(string pipeName)
    {
        return new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }
}
