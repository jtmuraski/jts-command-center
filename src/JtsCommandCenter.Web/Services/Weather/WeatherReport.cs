namespace JtsCommandCenter.Web.Services.Weather;

/// <summary>Current conditions plus a short daily forecast for one location.</summary>
/// <param name="ObservedAt">Local time (at the location) the current conditions apply to.</param>
public sealed record WeatherReport(
    string LocationName,
    string TemperatureUnit,
    CurrentWeather Current,
    IReadOnlyList<DailyForecast> Forecast,
    DateTime ObservedAt);

public sealed record CurrentWeather(
    int Temperature,
    int High,
    int Low,
    WeatherCondition Condition,
    string Description,
    bool IsDay);

public sealed record DailyForecast(
    DateOnly Date,
    WeatherCondition Condition,
    string Description,
    int High,
    int Low);
