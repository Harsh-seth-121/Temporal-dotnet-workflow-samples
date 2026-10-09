namespace Sandbox.Samples.Onboarding;

using Sandbox.Abstractions;
using Temporalio.Worker;

/// <summary>
/// Registers the onboarding workflows and activities with the shared worker.
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
