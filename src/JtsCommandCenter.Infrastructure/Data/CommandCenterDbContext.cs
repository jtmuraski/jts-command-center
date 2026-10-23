using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using JtsCommandCenter.Core.Projects;

namespace JtsCommandCenter.Infrastructure.Data;

/// <summary>
/// The app's database: SQL Server LocalDB in development, Azure SQL in production. Create a short-lived
/// instance per operation through <see cref="IDbContextFactory{TContext}"/>, since a Blazor circuit
/// outlives any single unit of work.
/// </summary>
internal sealed class CommandCenterDbContext(DbContextOptions<CommandCenterDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Subtask> Subtasks => Set<Subtask>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Link> Links => Set<Link>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored by name so the tables read well in a SQL tool.
        configurationBuilder.Properties<WorkArea>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ProjectStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<SubtaskStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Priority>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<NoteKind>().HaveConversion<string>().HaveMaxLength(20);

        // Timestamps are written as UTC; mark them UTC again when read back.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(project =>
        {
            project.Property(p => p.Title).HasMaxLength(ProjectMath.MaxTitleLength);
            project.Property(p => p.BlockedReason).HasMaxLength(ProjectMath.MaxReasonLength);
            project.Property(p => p.Version).IsConcurrencyToken();
            project.HasIndex(p => new { p.Status, p.SortOrder });

            project.HasMany(p => p.Subtasks).WithOne(s => s.Project).HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
            project.HasMany(p => p.Notes).WithOne().HasForeignKey(n => n.ProjectId).OnDelete(DeleteBehavior.Cascade);
            project.HasMany(p => p.Links).WithOne().HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);
            project.HasMany(p => p.Attachments).WithOne().HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Subtask>(subtask =>
        {
            subtask.Property(s => s.Title).HasMaxLength(ProjectMath.MaxTitleLength);
            subtask.Property(s => s.BlockedReason).HasMaxLength(ProjectMath.MaxReasonLength);
            subtask.Property(s => s.Version).IsConcurrencyToken();
            subtask.HasIndex(s => new { s.ProjectId, s.Status, s.SortOrder });

            // SQL Server rejects a second cascade path to Notes/Links/Attachments (Project → Subtask → Note),
            // so the service deletes a subtask's rows itself.
            subtask.HasMany(s => s.Notes).WithOne().HasForeignKey(n => n.SubtaskId).OnDelete(DeleteBehavior.ClientCascade);
            subtask.HasMany(s => s.Links).WithOne().HasForeignKey(l => l.SubtaskId).OnDelete(DeleteBehavior.ClientCascade);
            subtask.HasMany(s => s.Attachments).WithOne().HasForeignKey(a => a.SubtaskId).OnDelete(DeleteBehavior.ClientCascade);
        });

        modelBuilder.Entity<Attachment>(attachment =>
        {
            attachment.Property(a => a.Id).ValueGeneratedNever();
            attachment.Property(a => a.FileName).HasMaxLength(255);
            attachment.Property(a => a.ContentType).HasMaxLength(50);
            attachment.Property(a => a.StorageKey).HasMaxLength(64);
        });

        modelBuilder.Entity<Link>(link =>
        {
            link.Property(l => l.Label).HasMaxLength(ProjectMath.MaxLabelLength);
            link.Property(l => l.Url).HasMaxLength(ProjectMath.MaxUrlLength);
        });
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        value => value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
