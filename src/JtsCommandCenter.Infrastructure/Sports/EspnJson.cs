using System.Text.Json;
using JtsCommandCenter.Core.Sports;

namespace JtsCommandCenter.Infrastructure.Sports;

/// <summary>Small helpers for reading ESPN's loosely-shaped JSON, shared by the ESPN-backed services.</summary>
internal static class EspnJson
{
    public static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i) ? i : null;

    public static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    public static GameState ParseState(JsonElement statusType)
    {
        if (statusType.TryGetProperty("completed", out var completed) && completed.ValueKind == JsonValueKind.True)
        {
            return GameState.Final;
        }

        return Str(statusType, "state") switch
        {
            "in" => GameState.Live,
            "post" => GameState.Postponed, // over but not completed: postponed, suspended, or canceled
            _ => GameState.Scheduled
        };
    }

    // Schedule feeds use { "value": 3.0, "displayValue": "3" }; scoreboard feeds use a plain string.
    public static int? ParseScore(JsonElement competitor)
    {
        if (!competitor.TryGetProperty("score", out var score))
        {
            return null;
        }

        return score.ValueKind switch
        {
            JsonValueKind.Object when score.TryGetProperty("value", out var v) && v.TryGetDouble(out var d) => (int)d,
            JsonValueKind.String when int.TryParse(score.GetString(), out var i) => i,
            JsonValueKind.Number => score.GetInt32(),
            _ => null
        };
    }

    public static TimeZoneInfo ResolveZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    public static async Task<JsonDocument> GetJsonAsync(this HttpClient http, string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
