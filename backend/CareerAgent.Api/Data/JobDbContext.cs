using CareerAgent.Api.Jobs;
using Microsoft.EntityFrameworkCore;
namespace CareerAgent.Api.Data;

public sealed class JobDbContext(DbContextOptions<JobDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();
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
    }
}
