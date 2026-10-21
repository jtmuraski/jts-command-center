namespace JtsCommandCenter.Core.Weather;

public interface IWeatherService
{
    /// <summary>Returns the configured location's weather, served from cache when fresh.</summary>
    Task<WeatherReport> GetReportAsync(CancellationToken cancellationToken = default);
}
