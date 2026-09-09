namespace RevitMCP.Bridge;

public static class ProtocolVersionSelector
{
    public static int? SelectHighestCommon(IEnumerable<int> left, IEnumerable<int> right)
    {
        var common = left
            .Where(version => version > 0)
            .Distinct()
            .Intersect(right.Where(version => version > 0).Distinct())
            .ToArray();

        return common.Length == 0 ? null : common.Max();
    }
}
