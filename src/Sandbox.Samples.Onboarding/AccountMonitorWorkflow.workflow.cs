namespace Sandbox.Samples.Onboarding;

using Temporalio.Exceptions;
using Temporalio.Workflows;

/// <summary>
/// Watches an account for as long as it is asked to, started and then abandoned by
/// <see cref="OnboardingWorkflow"/>.
///
/// There is not a single patch in this file, and that is the point worth taking from
/// it. Continue-as-new closes the current run and opens a fresh one carrying the
/// state forward, so every boundary resets what the next deploy has to stay
/// compatible with: the new run starts on an empty history and simply runs whatever
/// code the worker has.
///
/// That is a bound, not an exemption, and the difference matters. A run that is
/// already mid-cycle is replayed by the new worker on its very next workflow task,
/// exactly like any other open workflow. A change that run's history cannot account
/// for fails that task and keeps failing it, so the run never reaches the boundary
/// that was supposed to save it. What the boundary buys is a short, known window:
/// hold the old workers until the cycle turns over, or patch the change anyway. An
/// hourly cycle makes waiting cheap, which is the whole reason to cycle hourly.
///
/// A wedged run is not a lost one, which is worth knowing before you panic. The task
/// keeps failing and keeps being retried, so deploying the old code back recovers it
/// with nothing else to do. Measured on this box: a monitor was wedged mid-cycle by an
/// added activity, sat failing its workflow task, and completed normally once the
/// change was rolled back.
///
/// The parent cannot make that trade at all. Its whole life is shorter than one
/// deploy, so it has nowhere to put a boundary and has to patch instead.
///
/// The other reason to continue as new is size. History grows with every timer and
/// every activity, and a run that never ends never stops growing. Cycling caps it.
/// </summary>
[Workflow]
public class AccountMonitorWorkflow
{
    [WorkflowRun]
    public async Task<MonitorResult> RunAsync(MonitorState state)
    {
        // Rejected rather than absorbed, and this is the one piece of validation the
        // sample needs. Counting checks instead of timing them means a cycle lasts as
        // long as its delays add up to, so a CheckInterval of zero makes a cycle
        // instantaneous: DelayAsync(0) becomes a 1ms timer, the single check finishes,
        // and the run continues as new. With MaxCycles 0 nothing then stops it. Not the
        // parent, which abandoned it; not ContinueAsNewSuggested, because every cycle
        // starts a fresh history; and no timeout, because none is set. Measured before
        // this guard existed: five nominal one-hour cycles completed in under a second.
        //
        // Non-retryable, so the monitor fails once and says why instead of retrying a
        // configuration that cannot become valid.
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

            // The count below decides the ordinary cycle boundary. This decides the
            // one that matters on a busy box: the server says history is getting
            // large, and cutting the cycle short is cheaper than being told later
            // that it is too large to continue.
            if (Workflow.ContinueAsNewSuggested)
            {
                break;
            }
        }

        var cyclesDone = state.Cycle + 1;

        // MaxCycles 0 is the production shape: watch until something stops it. Any
        // other value bounds the run, which is what keeps the demo and the tests from
        // leaving a workflow going for the rest of the retention period.
        if (state.Settings.MaxCycles > 0 && cyclesDone >= state.Settings.MaxCycles)
        {
            return new MonitorResult(state.AccountId, state.ChecksDone, cyclesDone);
        }

        // Built on its own line because the call below takes an expression tree, and a
        // `with` expression cannot appear inside one (CS8849).
        var next = state with { Cycle = cyclesDone };

        // Thrown, not returned. The SDK reads this exception as a command to close
        // this run and start the next one with the arguments given here, so anything
        // not passed through is lost at the boundary. ChecksDone riding along is what
        // makes the total meaningful across the whole execution.
        throw Workflow.CreateContinueAsNewException(
            (AccountMonitorWorkflow wf) => wf.RunAsync(next));
    }

    /// <summary>
    /// How many checks fit in one cycle.
    ///
    /// Counted rather than timed. Subtracting <c>Workflow.Info.StartTime</c> from
    /// <c>Workflow.UtcNow</c> would also work and is deterministic, but it invites the
    /// wrong reading: StartTime is when this run started, not when the execution did,
    /// and after a continue-as-new those are different instants. Arithmetic on the
    /// input has no such trap and gives the same answer on every replay.
    ///
    /// The non-positive branch keeps this total, because it is public and the client
    /// calls it outside any workflow to print what a run will do. It is a guard against
    /// dividing by zero and nothing more: it decides no policy, and a monitor never
    /// reaches it with such a value because RunAsync rejects one first.
    /// </summary>
    public static int ChecksPerCycle(MonitorSettings settings) =>
        settings.CheckInterval <= TimeSpan.Zero
            ? 1
            : Math.Max(1, (int)(settings.CycleLength.Ticks / settings.CheckInterval.Ticks));
}
