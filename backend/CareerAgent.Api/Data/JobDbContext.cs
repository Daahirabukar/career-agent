using CareerAgent.Api.Jobs;
using CareerAgent.Api.Discovery;
using CareerAgent.Api.Documents;
using Microsoft.EntityFrameworkCore;
namespace CareerAgent.Api.Data;

public sealed class JobDbContext(DbContextOptions<JobDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobOccurrence> JobOccurrences => Set<JobOccurrence>();
    public DbSet<DiscoveryRun> DiscoveryRuns => Set<DiscoveryRun>();
    public DbSet<ReferenceDocument> ReferenceDocuments => Set<ReferenceDocument>();
    public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();
    public DbSet<GeneratedDocumentVersion> GeneratedDocumentVersions => Set<GeneratedDocumentVersion>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<Job>();
        job.HasKey(x => x.Id);
        job.Property(x => x.Title).HasMaxLength(200).IsRequired();
        job.Property(x => x.Company).HasMaxLength(200).IsRequired();
        job.Property(x => x.Location).HasMaxLength(200).IsRequired();
        job.Property(x => x.SourceUrl).HasMaxLength(2048).IsRequired();
        job.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        job.Property(x => x.CreatedAt).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        job.HasIndex(x => x.Status);
        job.HasIndex(x => x.CanonicalUrl);
        job.HasIndex(x => x.Score);
        var occurrence = modelBuilder.Entity<JobOccurrence>();
        occurrence.HasIndex(x => new { x.SourceId, x.ExternalId }).IsUnique();
        occurrence.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<DiscoveryRun>().HasIndex(x => x.CreatedAt);
        var reference = modelBuilder.Entity<ReferenceDocument>();
        reference.HasKey(x => x.Id);
        reference.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        reference.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        reference.Property(x => x.FileName).HasMaxLength(100).IsRequired();
        reference.Property(x => x.TextFileName).HasMaxLength(100).IsRequired();
        reference.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        reference.HasIndex(x => x.CreatedAt);
        var document = modelBuilder.Entity<GeneratedDocument>();
        document.HasKey(x => x.Id);
        document.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        document.HasIndex(x => new { x.JobId, x.Kind }).IsUnique();
        document.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        var version = modelBuilder.Entity<GeneratedDocumentVersion>();
        version.HasKey(x => x.Id);
        version.Property(x => x.ContentFileName).HasMaxLength(100).IsRequired();
        version.Property(x => x.ProfileVersion).HasMaxLength(32).IsRequired();
        version.Property(x => x.ReferenceIdsJson).HasMaxLength(4000).IsRequired();
        version.Property(x => x.Engine).HasMaxLength(80).IsRequired();
        version.HasIndex(x => new { x.DocumentId, x.Number }).IsUnique();
        version.HasOne<GeneratedDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        // SQLite loses DateTime.Kind; every timestamp in this application is UTC.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
        {
            if (property.ClrType == typeof(DateTime))
                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            if (property.ClrType == typeof(DateTime?))
                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null));
        }
    }
}
