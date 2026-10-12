using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Shop.Micro.Catalog.Api.Data;
using Shop.Micro.Catalog.Api.Errors;
using Shop.Micro.Catalog.Api.Products;
using Shop.Micro.Catalog.Api.Stock;
using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Messaging;
using Shop.Micro.ServiceDefaults;

// The Catalog service: 04's Catalog module in a process of its own. What is new is only around the edges:
// its own database, the broker instead of an in-process bus, and the service defaults. Guide: §8.2.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Connection strings come from the AppHost ("catalogdb", "messaging"). Retries off: the messaging building
// block opens its own transactions, which EF Core's retrying strategy does not allow.
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb", settings => settings.DisableRetry = true);
builder.AddRabbitMQClient("messaging");
builder.Services.AddMessaging<CatalogDbContext>("catalog")
    .Consume<ReserveStock, ReserveStockConsumer>()
    .Consume<ReleaseStock, ReleaseStockConsumer>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Development only: in production, migrations are a deployment step, not something every instance races to do.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();
ProductEndpoints.Map(app);
ProductSnapshots.Map(app);

await app.RunAsync();
