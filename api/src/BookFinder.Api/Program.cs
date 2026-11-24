using System.Text.Json.Serialization;
using BookFinder.Api.Configuration;
using BookFinder.Api.Endpoints;
using BookFinder.Application;
using BookFinder.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Hosts like Render pass the port in PORT and expect us on all interfaces.
if (builder.Configuration["PORT"] is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Missing data is left out of responses rather than sent as null (e.g. firstPublishYear).
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddProblemDetails();
builder.Services.AddConfiguredCors();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.MapHealthEndpoints();

app.Run();

// Exposed for WebApplicationFactory in tests.
public partial class Program;
