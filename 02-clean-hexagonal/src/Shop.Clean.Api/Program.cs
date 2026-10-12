using System.Text.Json.Serialization;
using Shop.Clean.Api.Endpoints;
using Shop.Clean.Api.ErrorHandling;
using Shop.Clean.Application;
using Shop.Clean.Infrastructure;

// Composition root of the clean version: the only place that knows every layer. It registers the use cases
// (Application) and plugs the adapters into the ports (Infrastructure). Everything else depends inwards.
// Guide: §5.2.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateDatabaseAsync();
}

app.MapProductEndpoints();
app.MapOrderEndpoints();
app.MapPaymentEndpoints();

await app.RunAsync();
