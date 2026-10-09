using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class GetWarningsRoutingTests
{
    [Fact]
    public void Get_warnings_eligibility_is_protocol_v11_only()
    {
        Assert.False(InstanceTargetResolver.IsGetWarningsEligible(TestSupport.Ready("v10", "pipe-v10", protocolVersion: 10)));
        Assert.True(InstanceTargetResolver.IsGetWarningsEligible(TestSupport.Ready("v11", "pipe-v11", protocolVersion: 11)));
        Assert.False(InstanceTargetResolver.IsGetWarningsEligible(TestSupport.Ready("v12", "pipe-v12", protocolVersion: 12)));
    }
}
