using System.Text.Json;

namespace BimIt.Revit.Assistant.Ipc;

/// <summary>
/// Loaded-type search from Pattern Review. The request names a role and a search text. The result lists
/// bounded local option keys that the Assistant added to the current pattern generation.
/// </summary>
public static class RoomPatternTypeSearch
{
    public const int MaxQueryLength = 80;

    public const int MaxOptions = 80;

    public const int MaxResultCharacters = 64_000;

    public static string SerializeRequest(string role, string query)
    {
        return JsonSerializer.Serialize(new Request { Role = role, Query = query }, RoomPatternReview.Options);
    }

    public static bool TryParseRequest(string? json, out string role, out string query)
    {
        role = "";
        query = "";
        if (string.IsNullOrWhiteSpace(json) || json.Length > 400)
        {
            return false;
        }

        try
        {
            var body = JsonSerializer.Deserialize<Request>(json, RoomPatternReview.Options);
            if (body is null || !RoomPatternReview.IsRole(body.Role) || !IsQuery(body.Query))
            {
                return false;
            }

            role = body.Role!;
            query = body.Query!.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? SerializeResult(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var json = JsonSerializer.Serialize(result, RoomPatternReview.Options);
        return TryParseResult(json, out _) ? json : null;
    }

    public static bool TryParseResult(string? json, out JsonElement result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxResultCharacters)
        {
            return false;
        }

        try
        {
            var body = JsonSerializer.Deserialize<Result>(json, RoomPatternReview.Options);
            if (body is null
                || body.Generation is null
                || !RoomPatternReview.IsGeneration(body.Generation)
                || !RoomPatternReview.IsRole(body.Role)
                || !IsQuery(body.Query)
                || body.Options is null
                || body.Options.Count > MaxOptions
                || body.Options.Any(option => !RoomPatternReview.IsOption(option))
                || body.Options.Select(option => option.Key).Distinct(StringComparer.Ordinal).Count() != body.Options.Count)
            {
                return false;
            }

            result = JsonSerializer.SerializeToElement(body, RoomPatternReview.Options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsQuery(string? query)
    {
        return !string.IsNullOrWhiteSpace(query)
            && query.Trim().Length <= MaxQueryLength
            && !query.Any(char.IsControl);
    }

    public sealed class Request
    {
        public string? Role { get; set; }

        public string? Query { get; set; }
    }

    public sealed class Result
    {
        public string? Generation { get; set; }

        public string? Role { get; set; }

        public string? Query { get; set; }

        public bool Truncated { get; set; }

        public List<RoomPatternReview.TypeOption>? Options { get; set; }
    }
}
