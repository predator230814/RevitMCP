namespace BimIt.Revit.Assistant.Ipc;

/// <summary>
/// Reads one chat turn: zero or more <c>chatActivity</c> frames, then exactly one matching <c>chatResult</c>.
/// An activity is never returned as the turn result. Frames for another request are ignored.
/// </summary>
public static class AssistantChatExchange
{
    public const int MaxIgnoredFrames = 64;

    public static async Task<AssistantIpcMessage?> ReadUntilResultAsync(
        Stream stream,
        string requestId,
        Action<AssistantIpcMessage>? onActivity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var ignored = 0;
        while (true)
        {
            var json = await AssistantPipeFrames.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (json is null)
            {
                return null;
            }

            var parsed = AssistantIpc.Parse(json);
            if (parsed.Accepted && parsed.Message is not null)
            {
                var message = parsed.Message;
                if (message.Kind == AssistantIpcKind.ChatActivity)
                {
                    if (string.Equals(message.RequestId, requestId, StringComparison.Ordinal))
                    {
                        onActivity?.Invoke(message);
                    }
                    else if (++ignored > MaxIgnoredFrames)
                    {
                        return null;
                    }

                    continue;
                }

                if (message.Kind == AssistantIpcKind.ChatResult
                    && string.Equals(message.RequestId, requestId, StringComparison.Ordinal))
                {
                    return message;
                }
            }

            if (++ignored > MaxIgnoredFrames)
            {
                return null;
            }
        }
    }
}
