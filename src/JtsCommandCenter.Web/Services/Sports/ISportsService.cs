namespace JtsCommandCenter.Web.Services.Sports;

public interface ISportsService
{
    /// <summary>Returns schedules for the configured teams, served from cache when fresh.</summary>
    Task<SportsReport> GetReportAsync(CancellationToken cancellationToken = default);
}
