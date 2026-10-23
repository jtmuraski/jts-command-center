using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using JtsCommandCenter.Core.News;
using JtsCommandCenter.Core.Projects;
using JtsCommandCenter.Core.Sports;
using JtsCommandCenter.Core.Weather;
using JtsCommandCenter.Infrastructure.Data;
using JtsCommandCenter.Infrastructure.News;
using JtsCommandCenter.Infrastructure.Projects;
using JtsCommandCenter.Infrastructure.Sports;
using JtsCommandCenter.Infrastructure.Weather;

namespace JtsCommandCenter.Infrastructure;

/// <summary>Registers the Infrastructure implementations of the Core service interfaces.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Name of the database connection string under "ConnectionStrings".</summary>
    public const string ConnectionStringName = "CommandCenter";

    private const string EspnBaseAddress = "https://site.api.espn.com/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    // Some feed hosts reject requests that carry no user agent.
    private const string FeedUserAgent = "JtsCommandCenter/1.0 (+personal dashboard)";

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
        services.AddHttpClient<IGameDetailService, EspnGameDetailService>(client =>
        {
            client.BaseAddress = new Uri(EspnBaseAddress);
            client.Timeout = RequestTimeout;
        });
        services.AddHttpClient<ISportsNewsService, SportsNewsService>(client =>
        {
            // ESPN relative URLs resolve against this; RSS feeds are fetched by absolute URL.
            client.BaseAddress = new Uri(EspnBaseAddress);
            client.Timeout = RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(FeedUserAgent);
        });

        return services;
    }

    /// <summary>General news by category, merged from the RSS/Atom feeds in the "News" section.</summary>
    public static IServiceCollection AddNews(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<NewsOptions>(configuration.GetSection(NewsOptions.SectionName));
        services.AddHttpClient<INewsService, NewsService>(client =>
        {
            client.Timeout = RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(FeedUserAgent);
        });

        return services;
    }

    /// <summary>
    /// Projects, subtasks, and notes in SQL Server (SQL Express in development, Azure SQL in production),
    /// using the "CommandCenter" connection string and the "Projects" section; image attachments in a
    /// local folder or Azure Blob Storage, per the "Attachments" section.
    /// </summary>
    public static IServiceCollection AddProjects(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. Set ConnectionStrings:{ConnectionStringName} in configuration.");

        services.Configure<ProjectsOptions>(configuration.GetSection(ProjectsOptions.SectionName));
        services.AddDbContextFactory<CommandCenterDbContext>(options =>
            // Retries cover transient Azure SQL errors, including a serverless database waking up.
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IProjectService, ProjectService>();

        var attachments = configuration.GetSection(AttachmentOptions.SectionName);
        services.Configure<AttachmentOptions>(attachments);
        services.AddSingleton<IAttachmentService, AttachmentService>();
        if (string.Equals(attachments[nameof(AttachmentOptions.Provider)], AttachmentOptions.AzureBlobProvider, StringComparison.OrdinalIgnoreCase))
        {
            var settings = attachments.Get<AttachmentOptions>() ?? new AttachmentOptions();
            var serviceUri = settings.BlobServiceUri
                ?? throw new InvalidOperationException("Attachments:BlobServiceUri is required when Attachments:Provider is AzureBlob.");

            // Signs in with the App Service's managed identity (or your az/Visual Studio login locally).
            var container = new BlobServiceClient(new Uri(serviceUri), new DefaultAzureCredential())
                .GetBlobContainerClient(settings.ContainerName);
            services.AddSingleton<IAttachmentStore>(new BlobAttachmentStore(container));
        }
        else
        {
            services.AddSingleton<IAttachmentStore, LocalAttachmentStore>();
        }

        return services;
    }

    /// <summary>
    /// Applies any pending EF Core migrations. Meant for local development; deployed environments get
    /// migrations from the deployment pipeline instead.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<CommandCenterDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
