namespace JtsCommandCenter.Web.Services.Weather;

/// <summary>Settings bound from the "Weather" section of appsettings.json.</summary>
public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    public string LocationName { get; set; } = "Howard Lake, MN";
    public double Latitude { get; set; } = 45.0608;
    public double Longitude { get; set; } = -94.073;

    /// <summary>"fahrenheit" or "celsius", passed straight through to Open-Meteo.</summary>
    public string TemperatureUnit { get; set; } = "fahrenheit";

    /// <summary>"mph", "kmh", "ms", or "kn", passed straight through to Open-Meteo.</summary>
    public string WindSpeedUnit { get; set; } = "mph";

    /// <summary>"inch" or "mm". With "mm", Open-Meteo reports snowfall in centimeters.</summary>
    public string PrecipitationUnit { get; set; } = "inch";

    /// <summary>Number of upcoming days shown in the forecast row (not counting today).</summary>
    public int ForecastDays { get; set; } = 5;

    /// <summary>How long a fetched report is reused before calling Open-Meteo again.</summary>
    public int CacheMinutes { get; set; } = 10;

    /// <summary>How often an open dashboard re-requests weather.</summary>
    public int RefreshMinutes { get; set; } = 15;
}
