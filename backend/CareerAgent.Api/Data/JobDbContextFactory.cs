using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CareerAgent.Api.Data;

// Keep EF migration scaffolding independent from API startup and its background worker.
public sealed class JobDbContextFactory : IDesignTimeDbContextFactory<JobDbContext>
{
    public JobDbContext CreateDbContext(string[] args)
    {
        var dataSource = Environment.GetEnvironmentVariable("ConnectionStrings__Jobs")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../data/career-agent.db"));
        var options = new DbContextOptionsBuilder<JobDbContext>()
            .UseSqlite(dataSource.Contains('=') ? dataSource : $"Data Source={dataSource}")
            .Options;
        return new JobDbContext(options);
    }
}
