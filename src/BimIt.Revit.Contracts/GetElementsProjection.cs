using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetElementsProjection
{
    [JsonPropertyName("fields")]
    public IReadOnlyList<GetElementField>? Fields { get; init; }

    [JsonPropertyName("parameter_names")]
    public IReadOnlyList<string>? ParameterNames { get; init; }
}
