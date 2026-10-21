namespace JtsCommandCenter.Tests.Support;

/// <summary>Builds minimal ESPN-shaped JSON containing only the fields the services read.</summary>
public static class EspnFixtures
{
    public static string Team(string id, string abbreviation, string displayName, string name) =>
        $$"""{ "id": "{{id}}", "abbreviation": "{{abbreviation}}", "displayName": "{{displayName}}", "name": "{{name}}" }""";

    /// <param name="state">ESPN status state: "pre", "in", or "post".</param>
    /// <param name="ourRecord">Our team's record after the game, e.g. "77-85" (final games only).</param>
    public static string Event(
        string id,
        string utcDate,
        string ourId,
        string opponentId,
        string opponentAbbreviation,
        bool home,
        string state,
        bool completed,
        int? ourScore = null,
        int? theirScore = null,
        int seasonType = 2,
        string? ourRecord = null,
        string? week = null)
    {
        var shortDetail = completed ? "Final" : state == "in" ? "Q3 4:12" : "Scheduled";
        var ourScoreJson = ourScore is int us ? $$""", "score": { "value": {{us}}.0, "displayValue": "{{us}}" }""" : "";
        var theirScoreJson = theirScore is int them ? $$""", "score": { "value": {{them}}.0, "displayValue": "{{them}}" }""" : "";
        var recordJson = ourRecord is null ? "" : $$""", "record": [ { "type": "total", "displayValue": "{{ourRecord}}" } ]""";
        var weekJson = week is null ? "" : $$""", "week": { "text": "{{week}}" }""";

        return $$"""
        {
          "id": "{{id}}",
          "date": "{{utcDate}}",
          "seasonType": { "type": {{seasonType}} }{{weekJson}},
          "competitions": [ {
            "timeValid": true,
            "status": { "type": { "state": "{{state}}", "completed": {{(completed ? "true" : "false")}}, "shortDetail": "{{shortDetail}}" } },
            "competitors": [
              { "id": "{{ourId}}", "homeAway": "{{(home ? "home" : "away")}}", "team": { "id": "{{ourId}}", "abbreviation": "MIN", "displayName": "Minnesota" }{{ourScoreJson}}{{recordJson}} },
              { "id": "{{opponentId}}", "homeAway": "{{(home ? "away" : "home")}}", "team": { "id": "{{opponentId}}", "abbreviation": "{{opponentAbbreviation}}", "displayName": "{{opponentAbbreviation}} Team" }{{theirScoreJson}} }
            ]
          } ]
        }
        """;
    }

    /// <param name="leagueSeasonType">The league's current season type: 2 regular, 3 postseason, 4 offseason.</param>
    public static string Schedule(string team, int year, int leagueSeasonType, string seasonLabel, string? standingSummary, params string[] events) =>
        $$"""
        {
          "team": {{InsertStanding(team, standingSummary)}},
          "season": { "year": {{year}}, "type": {{leagueSeasonType}}, "displayName": "{{seasonLabel}}" },
          "events": [ {{string.Join(",", events)}} ]
        }
        """;

    private static string InsertStanding(string team, string? standingSummary) =>
        standingSummary is null ? team : team.TrimEnd().TrimEnd('}') + $$""", "standingSummary": "{{standingSummary}}" }""";
}
