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
var runtime = new TemporalRuntime(new TemporalRuntimeOptions());

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
