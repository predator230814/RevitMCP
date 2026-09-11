using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

internal sealed class ProjectedStringJsonConverter : JsonConverter<ProjectedString>
{
    public override ProjectedString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return ProjectedString.Unavailable;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return ProjectedString.Of(reader.GetString()!);
        }

        throw new JsonException("Projected string values must be a JSON string or null.");
    }

    public override void Write(Utf8JsonWriter writer, ProjectedString value, JsonSerializerOptions options)
    {
        if (!value.IsRequested)
        {
            throw new InvalidOperationException("Omitted projected strings must not be serialized.");
        }

        if (value.Value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value);
    }
}
