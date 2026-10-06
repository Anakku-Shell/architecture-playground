using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Shop.Micro.Contracts.Payments;
using Shop.Micro.Messaging;
using Shop.Micro.Payments.Api;
using Shop.Micro.Payments.Api.Data;
using Shop.Micro.Payments.Api.Errors;
using Shop.Micro.Payments.Api.Features;
using Shop.Micro.ServiceDefaults;

// The Payments service: 04's Payments module (vertical slices) in a process of its own. Guide: §8.2.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Retries off: the messaging building block opens its own transactions (see the Catalog service).
builder.AddNpgsqlDbContext<PaymentsDbContext>("paymentsdb", settings => settings.DisableRetry = true);
builder.AddRabbitMQClient("messaging");
builder.Services.AddMessaging<PaymentsDbContext>("payments")
    .Consume<ProcessPayment, ProcessPaymentConsumer>();

builder.Services.AddSingleton<FakePaymentGateway>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Development only: in production, migrations are a deployment step.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();
GetPayment.Map(app);

await app.RunAsync();
