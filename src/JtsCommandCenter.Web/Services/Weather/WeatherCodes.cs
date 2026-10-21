namespace JtsCommandCenter.Web.Services.Weather;

/// <summary>
/// Maps WMO weather interpretation codes (used by Open-Meteo) to our icon set and a short description.
/// See https://open-meteo.com/en/docs ("WMO Weather interpretation codes").
/// </summary>
public static class WeatherCodes
{
    public static WeatherCondition ToCondition(int code) => code switch
    {
        0 or 1 => WeatherCondition.Clear,
        2 => WeatherCondition.PartlyCloudy,
        3 => WeatherCondition.Cloudy,
        45 or 48 => WeatherCondition.Fog,
        >= 51 and <= 67 or >= 80 and <= 82 => WeatherCondition.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherCondition.Snow,
        >= 95 and <= 99 => WeatherCondition.Thunderstorm,
        _ => WeatherCondition.Cloudy
    };

    public static string ToDescription(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly Clear",
        2 => "Partly Cloudy",
        3 => "Overcast",
        45 => "Fog",
        48 => "Freezing Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing Drizzle",
        61 => "Light Rain",
        63 => "Rain",
        65 => "Heavy Rain",
        66 or 67 => "Freezing Rain",
        71 => "Light Snow",
        73 => "Snow",
        75 => "Heavy Snow",
        77 => "Snow Grains",
        80 or 81 => "Rain Showers",
        82 => "Heavy Showers",
        85 or 86 => "Snow Showers",
        95 => "Thunderstorms",
        96 or 99 => "Thunderstorms with Hail",
        _ => "Unknown"
    };
}
