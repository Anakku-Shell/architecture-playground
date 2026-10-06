using Shop.Micro.ServiceDefaults;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

// The API gateway: clients see one address and the same paths as in 01-04; YARP (Yet Another Reverse
// Proxy) forwards each path to the service that owns it. Destinations are service names, resolved by
// service discovery to whatever address Aspire gave each service. Guide: §8.2.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

(string Service, string PathPrefix)[] routes =
[
    ("catalog", "/api/products"),
    ("ordering", "/api/orders"),
    ("payments", "/api/payments"),
];

builder.Services.AddReverseProxy()
    .LoadFromMemory(
        [.. routes.Select(r => new RouteConfig
        {
            RouteId = r.Service,
            ClusterId = r.Service,
            // "{**rest}" matches the prefix itself and everything below it ("/api/orders/{id}/pay").
            Match = new RouteMatch { Path = r.PathPrefix + "/{**rest}" },
        })],
        [.. routes.Select(r => new ClusterConfig
        {
            ClusterId = r.Service,

            // A service that does not answer must not hold the client for YARP's default 100 seconds: after
            // 10 seconds without activity the gateway answers 504 Gateway Timeout itself.
            HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(10) },
            Destinations = new Dictionary<string, DestinationConfig>
            {
                [r.Service] = new() { Address = $"http://{r.Service}" },
            },
        })])
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapReverseProxy();

await app.RunAsync();
