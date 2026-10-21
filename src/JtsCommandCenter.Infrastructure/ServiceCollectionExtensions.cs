using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using JtsCommandCenter.Core.Sports;
using JtsCommandCenter.Core.Weather;
using JtsCommandCenter.Infrastructure.Sports;
using JtsCommandCenter.Infrastructure.Weather;

namespace JtsCommandCenter.Infrastructure;

/// <summary>Registers the Infrastructure implementations of the Core service interfaces.</summary>
public static class ServiceCollectionExtensions
{
    private const string EspnBaseAddress = "https://site.api.espn.com/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Weather from Open-Meteo, configured from the "Weather" section.</summary>
    public static IServiceCollection AddWeather(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<WeatherOptions>(configuration.GetSection(WeatherOptions.SectionName));
        services.AddHttpClient<IWeatherService, OpenMeteoWeatherService>(client =>
        {
            client.BaseAddress = new Uri("https://api.open-meteo.com/");
            client.Timeout = RequestTimeout;
        });

        return services;
    }

    /// <summary>
    /// ESPN team schedules (dashboard card), league scoreboards, and merged news feeds, configured from
    /// the "Sports" and "SportsCenter" sections.
    /// </summary>
    public static IServiceCollection AddSports(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<SportsOptions>(configuration.GetSection(SportsOptions.SectionName));
        services.Configure<SportsCenterOptions>(configuration.GetSection(SportsCenterOptions.SectionName));

        services.AddHttpClient<ISportsService, EspnSportsService>(client =>
        {
            client.BaseAddress = new Uri(EspnBaseAddress);
            client.Timeout = RequestTimeout;
        });
        services.AddHttpClient<IScoreboardService, EspnScoreboardService>(client =>
        {
            client.BaseAddress = new Uri(EspnBaseAddress);
            client.Timeout = RequestTimeout;
        });
        services.AddHttpClient<ISportsNewsService, SportsNewsService>(client =>
        {
            // ESPN relative URLs resolve against this; RSS feeds are fetched by absolute URL.
            client.BaseAddress = new Uri(EspnBaseAddress);
            client.Timeout = RequestTimeout;
            // Some feed hosts reject requests that carry no user agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("JtsCommandCenter/1.0 (+personal dashboard)");
        });

        return services;
    }
}
