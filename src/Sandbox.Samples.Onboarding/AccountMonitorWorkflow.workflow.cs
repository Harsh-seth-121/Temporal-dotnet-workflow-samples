namespace Sandbox.Samples.Onboarding;

using Temporalio.Exceptions;
using Temporalio.Workflows;

/// <summary>
/// Monitors account health across continue-as-new runs that cap event history.
/// Active runs must remain replay-compatible until their next boundary.
/// </summary>
[Workflow]
public class AccountMonitorWorkflow
{
    [WorkflowRun]
    public async Task<MonitorResult> RunAsync(MonitorState state)
    {
        // A non-positive interval would let an unbounded monitor continue as new without pause.
        if (state.Settings.CheckInterval <= TimeSpan.Zero)
        {
            throw new ApplicationFailureException(
                $"CheckInterval must be positive, got {state.Settings.CheckInterval}. " +
                "A zero or negative interval gives a cycle no duration, and an unbounded " +
                "monitor would then continue as new without pause.",
                nonRetryable: true);
        }

        var activity = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) };

        for (var check = 0; check < ChecksPerCycle(state.Settings); check++)
        {
            await Workflow.DelayAsync(state.Settings.CheckInterval);

            await Workflow.ExecuteActivityAsync(
                (MonitorActivities act) => act.CheckAccountHealth(state.AccountId), activity);

            state = state with { ChecksDone = state.ChecksDone + 1 };

            if (Workflow.ContinueAsNewSuggested)
            {
                break;
            }
        }

        var cyclesDone = state.Cycle + 1;

        if (state.Settings.MaxCycles > 0 && cyclesDone >= state.Settings.MaxCycles)
        {
            return new MonitorResult(state.AccountId, state.ChecksDone, cyclesDone);
        }

        var next = state with { Cycle = cyclesDone };

        throw Workflow.CreateContinueAsNewException(
            (AccountMonitorWorkflow wf) => wf.RunAsync(next));
    }

    /// <summary>
    /// Returns at least one account-health check per cycle for the supplied settings.
    /// </summary>
    public static int ChecksPerCycle(MonitorSettings settings) =>
        settings.CheckInterval <= TimeSpan.Zero
            ? 1
            : Math.Max(1, (int)(settings.CycleLength.Ticks / settings.CheckInterval.Ticks));
}
