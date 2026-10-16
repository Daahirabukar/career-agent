using System.Text.Json.Serialization;
using CareerAgent.Api.Data;
using CareerAgent.Api.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<JobStatus>(allowIntegerValues: false)));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
var databasePath = Path.Combine(builder.Environment.ContentRootPath, "data", "career-agent.db");
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
builder.Services.AddDbContext<JobDbContext>((services, options) => options.UseSqlite(
    services.GetRequiredService<IConfiguration>().GetConnectionString("Jobs")
    ?? new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()));
builder.Services.AddCors(options => options.AddPolicy("LocalFrontend", policy => policy
    .WithOrigins("http://localhost:5173").WithMethods("GET", "POST", "PATCH").WithHeaders("Content-Type")));

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
// Block browser requests from untrusted origins, including writes: CORS alone is not authentication.
app.Use(async (context, next) =>
{
    var origin = context.Request.Headers.Origin.ToString();
    if (origin.Length > 0 && !(app.Environment.IsDevelopment() && origin == "http://localhost:5173"))
    {
        await Results.Problem(statusCode: 403, title: "Denne oprindelse er ikke tilladt.").ExecuteAsync(context);
        return;
    }
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});
if (app.Environment.IsDevelopment()) app.UseCors("LocalFrontend");
app.MapGet("/api/health", async (JobDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok" })
        : Results.Problem(statusCode: 503, title: "Databasen er ikke tilgængelig."));
app.MapJobEndpoints();
app.Run();
public partial class Program { }
