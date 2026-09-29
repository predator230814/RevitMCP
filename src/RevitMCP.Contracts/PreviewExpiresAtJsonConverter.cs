using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class PreviewExpiresAtJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        var text = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
        if (text.EndsWith('.'))
        {
            text = text[..^1];
        }

        writer.WriteStringValue(text + "Z");
    }
}
