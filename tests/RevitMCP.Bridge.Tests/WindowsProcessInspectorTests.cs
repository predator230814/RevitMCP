using System.ComponentModel;
using System.Diagnostics;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class WindowsProcessInspectorTests
{
    [Fact]
    public void Process_access_failures_are_treated_as_not_live()
    {
        var inspector = new WindowsProcessInspector(_ => throw new Win32Exception(5, "Access is denied."));

        Assert.Null(inspector.GetProcess(1234));
    }

    [Fact]
    public void Missing_process_is_treated_as_not_live()
    {
        var inspector = new WindowsProcessInspector(_ => throw new ArgumentException("Process not found."));

        Assert.Null(inspector.GetProcess(999999));
    }

    [Fact]
    public void Current_process_can_be_inspected()
    {
        using var process = Process.GetCurrentProcess();
        var inspector = new WindowsProcessInspector();

        var snapshot = inspector.GetProcess(process.Id);

        Assert.NotNull(snapshot);
        Assert.Equal(process.Id, snapshot.ProcessId);
    }
}
