using System.Diagnostics;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class RegistrationFirstTeardownTests
{
    [Fact]
    public async Task Dispose_withdraws_registration_before_listener_teardown_completes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var withdrawnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWithdraw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new BlockingWithdrawStore(withdrawnStarted, releaseWithdraw);
        var process = Process.GetCurrentProcess();
        var metadata = TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId);

        await using var host = await NamedPipeBridgeHost.StartAsync(metadata, store, CancellationToken.None);
        Assert.NotNull(host.Registration);

        var dispose = host.DisposeAsync().AsTask();
        await withdrawnStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var stillListening = await NamedPipeBridgeClient.ConnectAsync(
            host.PipeName,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        var handshake = await stillListening.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = metadata.InstanceId,
                SupportedProtocolVersions = [1]
            },
            CancellationToken.None);
        Assert.Equal(metadata.InstanceId, handshake.InstanceId);

        releaseWithdraw.SetResult();
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));

        var timeout = await Assert.ThrowsAsync<BridgeException>(() =>
            NamedPipeBridgeClient.ConnectAsync(host.PipeName, TimeSpan.FromMilliseconds(400), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.HandshakeTimeout, timeout.ErrorCode);
        Assert.Null(host.Registration);
    }

    [Fact]
    public async Task Successful_dispose_leaves_no_registration_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var process = Process.GetCurrentProcess();
        var metadata = TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId);
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);
        var path = store.GetRegistrationPath(metadata.WindowsSessionId, metadata.InstanceId);

        var host = await NamedPipeBridgeHost.StartAsync(metadata, store, CancellationToken.None);
        Assert.True(File.Exists(path));

        await host.DisposeAsync();

        Assert.False(File.Exists(path));
        Assert.Null(host.Registration);
    }

    private sealed class BlockingWithdrawStore : IRegistrationStore
    {
        private readonly TaskCompletionSource _withdrawStarted;
        private readonly TaskCompletionSource _releaseWithdraw;

        public BlockingWithdrawStore(TaskCompletionSource withdrawStarted, TaskCompletionSource releaseWithdraw)
        {
            _withdrawStarted = withdrawStarted;
            _releaseWithdraw = releaseWithdraw;
        }

        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", "unused");

        public string GetRegistrationPath(int windowsSessionId, string instanceId) =>
            Path.Combine(RootPath, windowsSessionId.ToString(), instanceId + ".json");

        public Task<IRegistrationLease> PublishAsync(RevitInstanceRegistration registration, CancellationToken cancellationToken)
        {
            return Task.FromResult<IRegistrationLease>(new BlockingLease(registration, _withdrawStarted, _releaseWithdraw));
        }

        public Task<IReadOnlyList<RegistrationReadResult>> ReadSessionAsync(int windowsSessionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RegistrationReadResult>>([]);

        public Task RemoveAsync(int windowsSessionId, string instanceId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class BlockingLease : IRegistrationLease
    {
        private readonly TaskCompletionSource _withdrawStarted;
        private readonly TaskCompletionSource _releaseWithdraw;
        private int _disposed;

        public BlockingLease(
            RevitInstanceRegistration registration,
            TaskCompletionSource withdrawStarted,
            TaskCompletionSource releaseWithdraw)
        {
            Registration = registration;
            _withdrawStarted = withdrawStarted;
            _releaseWithdraw = releaseWithdraw;
        }

        public RevitInstanceRegistration Registration { get; }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _withdrawStarted.TrySetResult();
            await _releaseWithdraw.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
    }
}
