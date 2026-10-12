using System.Text.Json.Serialization;
using Shop.Micro.Ordering.Api.Http;
using Shop.Micro.Ordering.Infrastructure;
using Shop.Micro.ServiceDefaults;

// The Ordering service and its composition root: 04's Ordering module (clean/hexagonal) in a process of its
// own, and the orchestrator of the order saga. Guide: §8.2.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderingInfrastructure();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateOrderingDatabaseAsync();
}

app.MapDefaultEndpoints();
OrderEndpoints.Map(app);

await app.RunAsync();
