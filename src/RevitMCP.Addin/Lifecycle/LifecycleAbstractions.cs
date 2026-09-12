using RevitMCP.Bridge;

namespace RevitMCP.Addin.Lifecycle;

internal interface IBootstrapSubscription : IDisposable
{
}

internal interface IBootstrapScheduler
{
    IBootstrapSubscription Subscribe(Action<object> onBootstrap);
}

internal interface ILifecycleDispatcher : IDisposable
{
    void Stop();

    IRevitCapabilityService? CreateCapability(BridgeInstanceMetadata metadata);

    IRevitQueryElementsService? CreateQuery(BridgeInstanceMetadata metadata);

    IRevitGetElementsService? CreateGetElements(BridgeInstanceMetadata metadata);

    IRevitDescribeParametersService? CreateDescribeParameters(BridgeInstanceMetadata metadata);
}

internal interface ILifecycleBridge : IAsyncDisposable
{
    bool HasRegistration { get; }
}

internal interface ILifecycleDispatcherFactory
{
    ILifecycleDispatcher Create();
}

internal interface ILifecycleBridgeFactory
{
    ILifecycleBridge Start(
        BridgeInstanceMetadata metadata,
        IRevitCapabilityService? capability,
        IRevitQueryElementsService? query,
        IRevitGetElementsService? getElements,
        IRevitDescribeParametersService? describeParameters,
        CancellationToken cancellationToken);
}

internal interface IRuntimeMetadataSource
{
    BridgeInstanceMetadata Capture(string instanceId, LifecycleRevitRuntime revit);
}

internal readonly record struct LifecycleRevitRuntime(string VersionNumber, string VersionBuild);
