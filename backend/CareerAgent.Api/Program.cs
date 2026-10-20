using System.Text.Json.Serialization;
using CareerAgent.Api.Data;
using CareerAgent.Api.Discovery;
using CareerAgent.Api.Matching;
using CareerAgent.Api.Jobs;
using CareerAgent.Api.Documents;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<JobStatus>(allowIntegerValues: false)));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 11 * 1024 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 11 * 1024 * 1024;
    options.ValueLengthLimit = 16 * 1024;
    options.MultipartHeadersLengthLimit = 16 * 1024;
});
var databasePath = Path.Combine(builder.Environment.ContentRootPath, "data", "career-agent.db");
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
builder.Services.AddDbContext<JobDbContext>((services, options) => options.UseSqlite(
    services.GetRequiredService<IConfiguration>().GetConnectionString("Jobs")
    ?? new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()));
builder.Services.AddCors(options => options.AddPolicy("LocalFrontend", policy => policy
    .WithOrigins("http://localhost:5173").WithMethods("GET", "POST", "PATCH", "PUT", "DELETE").WithHeaders("Content-Type")));

builder.Services.AddSingleton<SourceCatalog>();
builder.Services.AddSingleton<ProfileStore>();
builder.Services.AddSingleton<AnalysisGate>();
builder.Services.AddSingleton<ICodexMatcher, CodexMatcher>();
builder.Services.AddSingleton<DocumentStorage>();
builder.Services.AddSingleton<IDocumentGenerator, CodexDocumentGenerator>();
builder.Services.AddSingleton<DocumentGenerationGate>();
builder.Services.AddSingleton<DiscoveryGate>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddHttpClient<IJobSourceClient, JobSourceClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(40);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CareerAgent/0.2 (personal local job reader)");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
if (builder.Configuration.GetValue("CareerAgent:WorkerEnabled", true)) builder.Services.AddHostedService<DiscoveryWorker>();
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
app.MapDiscovery();
app.MapMatching();
app.MapDocumentEndpoints();
app.Run();
public partial class Program { }
