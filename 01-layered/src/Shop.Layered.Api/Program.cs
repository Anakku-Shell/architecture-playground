using System.Text.Json.Serialization;
using Shop.Layered.Api.Endpoints;
using Shop.Layered.Api.ErrorHandling;
using Shop.Layered.Business;

// Composition root of the layered version: the Api (presentation layer) wires itself and asks the
// Business layer to register itself, which registers the Data layer in turn. Guide: §4.2.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopBusiness();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BusinessExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Turns exceptions into ProblemDetails (BusinessExceptionHandler) and empty error responses such as an
// unknown route into ProblemDetails too, so every error of the API has the same shape.
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
