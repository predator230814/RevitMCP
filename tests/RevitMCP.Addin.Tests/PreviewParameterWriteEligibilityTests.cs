using RevitMCP.Addin.Inspection;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class PreviewParameterWriteEligibilityTests
{
    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, true)]
    public void Write_eligibility_follows_the_amended_truth_table(
        bool isReadOnly,
        bool isShared,
        bool userModifiable,
        bool writable)
    {
        Assert.Equal(
            writable,
            PreviewParameterWriteEligibility.IsWritable(isReadOnly, isShared, userModifiable));
    }
}
