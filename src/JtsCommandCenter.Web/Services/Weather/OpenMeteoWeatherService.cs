using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JtsCommandCenter.Web.Services.Weather;

/// <summary>Weather from the free Open-Meteo forecast API (no API key required).</summary>
public sealed class OpenMeteoWeatherService(
    HttpClient http,
    IMemoryCache cache,
    IOptions<WeatherOptions> options,
    ILogger<OpenMeteoWeatherService> logger) : IWeatherService
{
    private const string CacheKey = "weather-report";

    private readonly WeatherOptions settings = options.Value;

    public async Task<WeatherReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out WeatherReport? cached) && cached is not null)
        {
            return cached;
        }

        var report = await FetchAsync(cancellationToken);
        cache.Set(CacheKey, report, TimeSpan.FromMinutes(settings.CacheMinutes));
        return report;
    }

    private async Task<WeatherReport> FetchAsync(CancellationToken cancellationToken)
    {
        // Invariant culture so coordinates always use '.' as the decimal separator.
        var url = FormattableString.Invariant(
            $"v1/forecast?latitude={settings.Latitude}&longitude={settings.Longitude}&current=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min&temperature_unit={settings.TemperatureUnit}&timezone=auto&forecast_days={settings.ForecastDays + 1}");

        logger.LogInformation("Fetching weather for {Location} from Open-Meteo", settings.LocationName);

        var response = await http.GetFromJsonAsync<OpenMeteoResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException("Open-Meteo returned an empty response.");

        var daily = response.Daily;
        var forecast = Enumerable.Range(0, daily.Time.Length)
            .Select(i => new DailyForecast(
                DateOnly.Parse(daily.Time[i], CultureInfo.InvariantCulture),
                WeatherCodes.ToCondition(daily.WeatherCode[i]),
                WeatherCodes.ToDescription(daily.WeatherCode[i]),
                Round(daily.TemperatureMax[i]),
                Round(daily.TemperatureMin[i])))
            .ToList();

        var today = forecast[0];
        var current = response.Current;

        return new WeatherReport(
            settings.LocationName,
            settings.TemperatureUnit,
            new CurrentWeather(
                Round(current.Temperature),
                today.High,
                today.Low,
                WeatherCodes.ToCondition(current.WeatherCode),
                WeatherCodes.ToDescription(current.WeatherCode),
                current.IsDay == 1),
            forecast.Skip(1).ToList(),
            DateTime.Parse(current.Time, CultureInfo.InvariantCulture));
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    // Shape of the Open-Meteo JSON response (only the fields we request).
    private sealed record OpenMeteoResponse(
        [property: JsonPropertyName("current")] OpenMeteoCurrent Current,
        [property: JsonPropertyName("daily")] OpenMeteoDaily Daily);

    private sealed record OpenMeteoCurrent(
        [property: JsonPropertyName("time")] string Time,
        [property: JsonPropertyName("temperature_2m")] double Temperature,
        [property: JsonPropertyName("weather_code")] int WeatherCode,
        [property: JsonPropertyName("is_day")] int IsDay);

    private sealed record OpenMeteoDaily(
        [property: JsonPropertyName("time")] string[] Time,
        [property: JsonPropertyName("weather_code")] int[] WeatherCode,
        [property: JsonPropertyName("temperature_2m_max")] double[] TemperatureMax,
        [property: JsonPropertyName("temperature_2m_min")] double[] TemperatureMin);
}
