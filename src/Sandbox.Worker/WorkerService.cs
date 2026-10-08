namespace Sandbox.Worker;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sandbox.Abstractions;
using Temporalio.Client;
using Temporalio.Runtime;
using Temporalio.Worker;

/// <summary>
/// Runs the Temporal worker for the lifetime of the host.
///
/// Hosted as a BackgroundService so the process responds to SIGTERM. Console
/// cancel handlers catch SIGINT only, and `docker stop` sends SIGTERM, so a
/// console-only worker gets killed after the daemon's grace period instead of
/// draining its activities.
/// </summary>
public sealed class WorkerService(
    SandboxConfig config,
    TemporalRuntime runtime,
    IReadOnlyList<ISampleModule> modules,
    IServiceProvider services,
    IHostApplicationLifetime lifetime,
    ILoggerFactory loggerFactory,
    ILogger<WorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunWorkerAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Worker stopped");
        }
        catch (Exception ex)
        {
            // A BackgroundService that throws stops the host, but the process would
            // still exit 0 and read as success to CI and to the smoke test. Record a
            // failing exit code before unwinding.
            logger.LogCritical(ex, "Worker failed");
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Connecting to {Address} (namespace {Namespace}, task queue {TaskQueue})",
            config.Address,
            config.Namespace,
            config.TaskQueue);

        var client = await TemporalClient.ConnectAsync(new(config.Address)
        {
            Namespace = config.Namespace,
            Runtime = runtime,
            LoggerFactory = loggerFactory,
        });

        var options = new TemporalWorkerOptions(config.TaskQueue)
        {
            GracefulShutdownTimeout = TimeSpan.FromSeconds(30),
        };

        foreach (var module in modules)
        {
            module.Register(options, services);
            logger.LogInformation("Registered sample module {Module}", module.Name);
        }

        using var worker = new TemporalWorker(client, options);

        logger.LogInformation("Worker running");

        // Returns a task that only ever faults. A clean shutdown surfaces as
        // cancellation, which ExecuteAsync above turns back into a normal stop.
        await worker.ExecuteAsync(stoppingToken);
    }
}
