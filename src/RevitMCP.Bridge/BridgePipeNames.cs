namespace RevitMCP.Bridge;

public static class BridgePipeNames
{
    public static string Create(int windowsSessionId, string instanceId)
    {
        return $"revitmcp.bridge.v1.s{windowsSessionId}.{ToPipeToken(instanceId)}";
    }

    private static string ToPipeToken(string instanceId)
    {
        if (Guid.TryParse(instanceId, out var guid))
        {
            return guid.ToString("N");
        }

        var token = new string(instanceId.Where(char.IsLetterOrDigit).ToArray());
        return token.Length > 0 ? token : Guid.NewGuid().ToString("N");
    }
}
