namespace BimIt.Revit.Contracts;

public readonly record struct SpatialPlanFrame(
    double OriginX,
    double OriginY,
    double AxisXx,
    double AxisXy,
    double AxisYx,
    double AxisYy)
{
    public static SpatialPlanFrame Identity { get; } = new(0, 0, 1, 0, 0, 1);

    public (double X, double Y) Apply(double x, double y)
    {
        return (
            OriginX + (x * AxisXx) + (y * AxisYx),
            OriginY + (x * AxisXy) + (y * AxisYy));
    }

    public (double X, double Y) ApplyVector(double x, double y)
    {
        return ((x * AxisXx) + (y * AxisYx), (x * AxisXy) + (y * AxisYy));
    }
}

public static class SpatialRoomRefCodec
{
    public const string HostPrefix = "sr1.h.";

    public const string LinkPrefix = "sr1.l.";

    public static string Host(string uniqueId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueId);
        return HostPrefix + uniqueId;
    }

    public static string Link(string linkUniqueId, string roomUniqueId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkUniqueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roomUniqueId);
        return LinkPrefix + linkUniqueId + "." + roomUniqueId;
    }

    public static bool TryHost(string? value, out string uniqueId)
    {
        uniqueId = "";
        if (value is null || !value.StartsWith(HostPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        uniqueId = value[HostPrefix.Length..];
        return uniqueId.Length is >= 1 and <= 120
            && uniqueId.IndexOf('\\') < 0
            && uniqueId.IndexOf('/') < 0
            && uniqueId.IndexOf(':') < 0;
    }

    public static bool IsLink(string? value)
    {
        return value is not null && value.StartsWith(LinkPrefix, StringComparison.Ordinal);
    }
}

public static class SpatialLinkLabel
{
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Link";
        }

        var value = name.Trim();
        var slash = Math.Max(value.LastIndexOf('\\'), value.LastIndexOf('/'));
        if (slash >= 0)
        {
            value = value[(slash + 1)..];
        }

        if (value.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        var colon = value.IndexOf(':');
        if (colon >= 0)
        {
            value = value[..colon];
        }

        value = value.Trim();
        if (value.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        value = value.Trim();
        if (value.Length == 0 || value.Any(character => character is '\\' or '/' or ':' || char.IsControl(character)))
        {
            return "Link";
        }

        return value.Length > 80 ? value[..80] : value;
    }
}
