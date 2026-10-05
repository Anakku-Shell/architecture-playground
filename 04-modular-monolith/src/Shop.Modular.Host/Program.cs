using System.Text.Json.Serialization;
using Shop.Modular.BuildingBlocks.Infrastructure;
using Shop.Modular.BuildingBlocks.Infrastructure.Modules;
using Shop.Modular.Catalog;
using Shop.Modular.Ordering.Infrastructure;
using Shop.Modular.Payments;

// The Host of the modular monolith: one process, three modules. This list IS the monolith. The Host knows
// each module only as an IModule; what is inside (CRUD, clean layers, slices) is the module's business.
// Guide: §7.2.
IModule[] modules = [new CatalogModule(), new OrderingModule(), new PaymentsModule()];

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddBuildingBlocks();
foreach (var module in modules)
{
    module.RegisterServices(builder.Services);
}

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Each module migrates its own schema, with its own migrations history table.
    foreach (var module in modules)
    {
        await module.MigrateAsync(app.Services, CancellationToken.None);
    }
}

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

await app.RunAsync();
