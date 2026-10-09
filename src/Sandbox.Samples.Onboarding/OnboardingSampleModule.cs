namespace Sandbox.Samples.Onboarding;

using Sandbox.Abstractions;
using Temporalio.Worker;

/// <summary>
/// Wires this sample into the shared worker. Discovered by reflection at startup.
///
/// Both workflows are registered on the one task queue. The parent starts the child
/// without naming a queue, so the child inherits this one and the same worker runs
/// both halves.
/// </summary>
public class OnboardingSampleModule : ISampleModule
{
    public string Name => "onboarding";

    public void Register(TemporalWorkerOptions options, IServiceProvider services)
    {
        options.AddWorkflow<OnboardingWorkflow>();
        options.AddWorkflow<AccountMonitorWorkflow>();

        options.AddAllActivities(new OnboardingActivities());
        options.AddAllActivities(new MonitorActivities());
    }
}
