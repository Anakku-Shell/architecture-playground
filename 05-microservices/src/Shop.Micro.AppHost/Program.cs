// The whole system of version 05, described in code. Each line is a process or a container; each
// WithReference gives one the address (or connection string) of another, and each WaitFor holds it back
// until the other reports healthy. Guide: §8.3.
var builder = DistributedApplication.CreateBuilder(args);

// One PostgreSQL server for convenience, but a separate DATABASE per service: no service can join
// another's tables, which is what "database per service" protects (Guide §8.2, ADR 0002).
var postgres = builder.AddPostgres("postgres");
var catalogDb = postgres.AddDatabase("catalogdb");
var orderingDb = postgres.AddDatabase("orderingdb");
var paymentsDb = postgres.AddDatabase("paymentsdb");

// The message broker, with its management UI (queues, bindings, dead letters) linked from the dashboard.
var messaging = builder.AddRabbitMQ("messaging").WithManagementPlugin();

var catalog = builder.AddProject<Projects.Shop_Micro_Catalog_Api>("catalog")
    .WithReference(catalogDb).WaitFor(catalogDb)
    .WithReference(messaging).WaitFor(messaging)
    .WithHttpHealthCheck("/health");

// Ordering also references catalog: it asks for names and prices over HTTP when an order is placed.
var ordering = builder.AddProject<Projects.Shop_Micro_Ordering_Api>("ordering")
    .WithReference(orderingDb).WaitFor(orderingDb)
    .WithReference(messaging).WaitFor(messaging)
    .WithReference(catalog)
    .WithHttpHealthCheck("/health");

var payments = builder.AddProject<Projects.Shop_Micro_Payments_Api>("payments")
    .WithReference(paymentsDb).WaitFor(paymentsDb)
    .WithReference(messaging).WaitFor(messaging)
    .WithHttpHealthCheck("/health");

// The gateway starts last: a service is healthy only once its queue is declared, so by the time clients
// can send requests every message has somewhere to go.
builder.AddProject<Projects.Shop_Micro_Gateway>("gateway")
    .WithReference(catalog).WaitFor(catalog)
    .WithReference(ordering).WaitFor(ordering)
    .WithReference(payments).WaitFor(payments)
    .WithHttpHealthCheck("/health");

await builder.Build().RunAsync();
