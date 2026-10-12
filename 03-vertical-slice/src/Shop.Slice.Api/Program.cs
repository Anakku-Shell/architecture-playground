using System.Text.Json.Serialization;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Infrastructure;

// Composition root of the vertical-slice version. Note what is missing compared with 02: no list of use
// cases and no list of endpoints. Each slice under Features/ registers its own route through IEndpoint,
// discovered here at startup. Guide: §6.2.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInfrastructure();
builder.Services.AddEndpoints(typeof(Program).Assembly);
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

app.MapEndpoints();

await app.RunAsync();
