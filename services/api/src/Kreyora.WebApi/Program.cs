using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Hangfire;
using Kreyora.Application;
using Kreyora.Infrastructure;
using Kreyora.Infrastructure.BackgroundJobs;
using Kreyora.Infrastructure.Correlation;
using Kreyora.Infrastructure.Errors;
using Kreyora.Infrastructure.Logging;
using Kreyora.Infrastructure.Persistence;
using Kreyora.ServiceDefaults;
using Kreyora.WebApi.Configuration;
using Kreyora.WebApi.Seeding;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.With<SensitiveDataEnricher>()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
        formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddServiceDefaults();

builder.Services
    .AddOptions<AppSettings>()
    .BindConfiguration(AppSettings.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<DatabaseSettings>()
    .BindConfiguration(DatabaseSettings.SectionName);

builder.Services
    .AddOptions<CorsSettings>()
    .BindConfiguration(CorsSettings.SectionName);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHangfireServices(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddOpenApi();

var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>();
if (corsSettings?.AllowedOrigins is { Length: > 0 })
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(corsSettings.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("X-Correlation-ID", "api-supported-versions");
        });
    });
}

var app = builder.Build();

if (args.Contains("--migrate"))
{
    await MigrationRunner.ApplyMigrationsAsync(app.Services);
    return;
}

if (args.Contains("--seed"))
{
    await DevSeedHook.SeedDevelopmentDataAsync(app.Services);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    if (app.Services.GetService<JobStorage>() is not null)
    {
        app.MapHangfireDashboard("/hangfire");
    }
}

app.UseCors();
app.UseHttpsRedirection();
app.MapServiceDefaults();
app.MapControllers();

app.Run();

namespace Kreyora.WebApi
{
    public partial class Program { }
}
