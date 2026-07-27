using Kreyora.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

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
