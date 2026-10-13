using System.IO.Pipes;
using BimIt.Revit.Contracts;
using StreamJsonRpc;

namespace BimIt.Revit.Bridge.Implementation;

internal static class JsonRpcFactory
{
    public static JsonRpc Create(Stream stream, object? localTarget)
    {
        var formatter = new SystemTextJsonFormatter
        {
            JsonSerializerOptions = ContractJson.CreateOptions()
        };
        var handler = new HeaderDelimitedMessageHandler(stream, formatter);
        var rpc = new JsonRpc(handler);
        if (localTarget is not null)
        {
            rpc.AddLocalRpcTarget(localTarget);
        }

        rpc.StartListening();
        return rpc;
    }

    public static NamedPipeServerStream CreateServer(string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
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
