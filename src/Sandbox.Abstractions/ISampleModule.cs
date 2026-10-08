namespace Sandbox.Abstractions;

using Microsoft.Extensions.DependencyInjection;
using Temporalio.Worker;

/// <summary>
/// One sample's contribution to the shared worker.
///
/// This is the seam that keeps samples additive. Sandbox.Worker discovers every
/// implementation at startup and never names a sample, so adding one means adding
/// a project and a project reference, not editing worker code.
/// </summary>
public interface ISampleModule
{
    /// <summary>Short name, used only in startup logging.</summary>
    string Name { get; }

    /// <summary>
    /// Register anything the sample's activities need from dependency injection.
    /// Runs before the host is built. Most samples have nothing to add.
    /// </summary>
    void ConfigureServices(IServiceCollection services)
    {
        // Intentionally empty. Override only when a sample needs its own services.
    }

    /// <summary>
    /// Add this sample's workflows and activities to the worker.
    /// Runs after the host is built, so <paramref name="services"/> can resolve
    /// anything registered in <see cref="ConfigureServices"/>.
    /// </summary>
    void Register(TemporalWorkerOptions options, IServiceProvider services);
}
