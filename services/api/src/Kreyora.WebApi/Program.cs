using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Kreyora.Application;
using Kreyora.Infrastructure;
using Kreyora.Infrastructure.Correlation;
using Kreyora.Infrastructure.Errors;
using Kreyora.Infrastructure.Logging;
using Kreyora.ServiceDefaults;
using Kreyora.WebApi.Configuration;
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
builder.Services.AddInfrastructure();

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

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapServiceDefaults();
app.MapControllers();

app.Run();

namespace Kreyora.WebApi
{
    public partial class Program { }
}
