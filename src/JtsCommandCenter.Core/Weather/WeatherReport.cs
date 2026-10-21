namespace JtsCommandCenter.Core.Weather;

/// <summary>Current conditions plus a short daily forecast for one location.</summary>
/// <param name="ObservedAt">Local time (at the location) the current conditions apply to.</param>
/// <param name="WindUnit">Display label for wind speeds, e.g. "mph".</param>
/// <param name="SnowUnit">Display label for snowfall amounts, e.g. "in".</param>
public sealed record WeatherReport(
    string LocationName,
    string TemperatureUnit,
    string WindUnit,
    string SnowUnit,
    CurrentWeather Current,
    IReadOnlyList<DailyForecast> Forecast,
    DateTime ObservedAt);

public sealed record CurrentWeather(
    int Temperature,
    int FeelsLike,
    int High,
    int Low,
    WeatherCondition Condition,
    string Description,
    bool IsDay,
    int Humidity,
    int WindSpeed,
    int WindGust,
    string WindDirection,
    int? PrecipitationChance,
    double Snowfall,
    TimeOnly Sunrise,
    TimeOnly Sunset);

/// <param name="PrecipitationChance">Max chance of precipitation for the day (0–100), if available.</param>
/// <param name="Snowfall">Expected snowfall for the day, in the report's <see cref="WeatherReport.SnowUnit"/>.</param>
public sealed record DailyForecast(
    DateOnly Date,
    WeatherCondition Condition,
    string Description,
    int High,
    int Low,
    int? PrecipitationChance,
    double Snowfall,
    TimeOnly Sunrise,
    TimeOnly Sunset);
