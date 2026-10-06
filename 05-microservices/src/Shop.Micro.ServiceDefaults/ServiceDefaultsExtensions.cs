using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Shop.Micro.ServiceDefaults;

/// <summary>
/// The defaults every service (and the gateway) applies with one call, adapted from the Aspire template.
/// Running many processes is only bearable if they all report logs, traces and health the same way, and
/// find each other by name instead of by port. Guide: §8.2.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// The name of the <see cref="System.Diagnostics.ActivitySource"/> the messaging library writes its spans
    /// to (publish and process). Listed here so the dashboard shows a message hop inside the order's trace.
    /// </summary>
    public const string MessagingActivitySource = "Shop.Micro.Messaging";

    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Retries, timeouts and a circuit breaker on every HttpClient (Guide §8.6, "Partial failure").
            http.AddStandardResilienceHandler();

            // "http://catalog" is resolved to the address Aspire gave the catalog service.
            http.AddServiceDiscovery();
        });

        return builder;
    }

    private static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .SetSampler(new BackgroundPollingSampler())
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(MessagingActivitySource)
                // Health probes every few seconds would bury the interesting traces.
                .AddAspNetCoreInstrumentation(options => options.Filter = context =>
                    !context.Request.Path.StartsWithSegments(HealthEndpointPath, StringComparison.Ordinal)
                    && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath, StringComparison.Ordinal))
                .AddHttpClientInstrumentation());

        // Aspire sets this variable to its dashboard; without it (a plain "dotnet run") nothing is exported.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    private static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // "live": the process answers. Other checks (database, messaging) are added by each service.
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    /// <summary>
    /// <c>/health</c>: every check passes, the service is ready for traffic (Aspire waits for it before starting
    /// what depends on the service). <c>/alive</c>: the process is up. Development only: they reveal internals.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        }

        return app;
    }
}
