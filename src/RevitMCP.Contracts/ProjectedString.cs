using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

[JsonConverter(typeof(ProjectedStringJsonConverter))]
public readonly struct ProjectedString : IEquatable<ProjectedString>
{
    public static ProjectedString Omitted => default;

    public static ProjectedString Unavailable { get; } = new(true, null);

    private ProjectedString(bool isRequested, string? value)
    {
        IsRequested = isRequested;
        Value = value;
    }

    public bool IsRequested { get; }

    public string? Value { get; }

    public static ProjectedString Of(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ProjectedString(true, value);
    }

    public static ProjectedString FromRequested(string? value)
    {
        return value is null ? Unavailable : Of(value);
    }

    public bool Equals(ProjectedString other)
    {
        return IsRequested == other.IsRequested && string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is ProjectedString other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsRequested, Value);
    }
}
