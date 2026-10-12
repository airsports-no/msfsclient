using System.Text.Json;
using System.Text.Json.Serialization;

namespace msfsclient.Core.Api;

/// <summary>
/// JSON settings for the Airsports API: snake_case names, unknown fields ignored, nulls left out when writing.
/// Every model property has a default, so a field missing from an older server never breaks parsing.
/// </summary>
public static class AirsportsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>The backend's "detail" explanation in an error body, or null when there is none.</summary>
    public static string? Detail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
