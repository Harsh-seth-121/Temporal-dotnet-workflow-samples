using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sandbox.Abstractions;
using Sandbox.Worker;
using Temporalio.Runtime;

var config = SandboxConfig.FromEnvironment();
var modules = SampleDiscovery.Discover();

if (modules.Count == 0)
{
    Console.Error.WriteLine(
        "No sample modules found. Every sample project must be referenced by " +
        "Sandbox.Worker.csproj and expose an ISampleModule implementation.");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

// Order matters. TemporalRuntime.Default is created lazily the first time a client
// is built without an explicit runtime; any client constructed before this line
// would bind to that default, and its metrics would never reach our endpoint.
// Build the runtime first, then hand it to every client.
var runtime = new TemporalRuntime(new TemporalRuntimeOptions
{
    Telemetry = new()
    {
        Metrics = new()
        {
            // Bind every interface: 127.0.0.1 would be unreachable from the
            // Prometheus container. Scrape path is /metrics and nothing else;
            // every other path returns an empty 404.
            Prometheus = new(config.MetricsAddress),

            // The naming flags are deliberately left alone. Core-based SDKs emit
            // durations in integer milliseconds and counters without a _total
            // suffix, and the dashboard written for those SDKs expects exactly
            // that. Switching to Prometheus-conventional naming would be more
            // idiomatic and would make every panel read empty.
            GlobalTags = new KeyValuePair<string, string>[]
            {
                new("service", "sandbox-worker"),
            },
        },
    },
});

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(runtime);
builder.Services.AddSingleton(modules);

foreach (var module in modules)
{
    module.ConfigureServices(builder.Services);
}

builder.Services.AddHostedService<WorkerService>();

await builder.Build().RunAsync();

// WorkerService sets this when it fails. Returning a literal 0 here would report
// success for a worker that never managed to connect.
return Environment.ExitCode;
