using System.IO;
using System.Text.Json;

namespace BimIt.Revit.Addin.CSharpQuery;

internal sealed class CSharpQueryEnablementStore
{
    public const int SchemaVersion = 1;

    public const string FileName = "capabilities.json";

    private readonly string _path;

    public CSharpQueryEnablementStore(string? root = null)
    {
        var directory = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP")
            : root;
        _path = Path.Combine(directory, FileName);
    }

    public bool IsEnabled()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            int? schema = null;
            bool? enabled = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("schema_version"))
                {
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var version))
                    {
                        return false;
                    }

                    schema = version;
                    continue;
                }

                if (property.NameEquals("csharp_query_enabled"))
                {
                    if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                    {
                        return false;
                    }

                    enabled = property.Value.GetBoolean();
                    continue;
                }

                return false;
            }

            return schema == SchemaVersion && enabled == true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
