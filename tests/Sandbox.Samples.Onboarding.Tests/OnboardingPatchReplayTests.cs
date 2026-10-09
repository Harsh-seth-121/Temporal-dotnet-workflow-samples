namespace Sandbox.Samples.Onboarding.Tests;

using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

/// <summary>
/// The tests that make the patch in OnboardingWorkflow mean something.
///
/// Every other test here runs the workflow forward from nothing, which is the one
/// case a patch cannot get wrong: with no history to contradict it, Patched returns
/// true and the new path runs. The branch that actually carries risk is the other
/// one, and it only runs against a history written before the patch existed.
///
/// So these tests write that history for real. A pre-patch copy of the workflow runs
/// under the production workflow type name, its history is fetched from the server,
/// and the current workflow is replayed against it. No fixture file, nothing to
/// regenerate by hand, and nothing that can drift out of date while still passing.
/// </summary>
public class OnboardingPatchReplayTests
{
    private static readonly MonitorSettings Settings = new(
        CheckInterval: TimeSpan.FromHours(1),
        CycleLength: TimeSpan.FromHours(1),
        MaxCycles: 1);

    /// <summary>
    /// A run that started before the welcome-email patch still replays cleanly against
    /// today's code.
    ///
    /// Two separate things have to hold for this to pass, which is why one test covers
    /// both. DeprecatePatch("screen-before-provision") has to accept a history that
    /// carries that marker. And Patched("email-welcome-instead-of-letter") has to
    /// return false against a history with no marker for it, so the else branch runs
    /// and queues a letter, matching what was recorded.
    ///
    /// Delete the else branch, or promote the second patch to DeprecatePatch too early,
    /// and this goes red. Nothing else in the repo would.
    /// </summary>
    [Fact]
    public async Task PrePatchHistoryStillReplaysAgainstTodaysWorkflow()
    {
        var cancel = TestContext.Current.CancellationToken;
        var history = await RecordAsync<OnboardingWorkflowBeforeEmailPatch>(cancel);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions().AddWorkflow<OnboardingWorkflow>());

        // Throws on any mismatch between the commands today's code issues and the
        // events recorded back then, which is exactly the failure a patch exists to
        // prevent.
        await replayer.ReplayWorkflowAsync(history, cancellationToken: cancel);
    }

    /// <summary>
    /// Runs one workflow to completion on a throwaway server and hands back its
    /// history.
    ///
    /// HoldOpenFor is zero deliberately. Both versions place their one conditional
    /// timer at the same point, so a non-zero value would replay too, but zero keeps
    /// the recorded history down to the events this test is actually about.
    ///
    /// The run starts a real monitor, which it abandons, and which is still running
    /// when the environment is torn down. MaxCycles is 1 and the first check is an
    /// hour out, so it never does any work.
    /// </summary>
    private static async Task<Temporalio.Common.WorkflowHistory> RecordAsync<TWorkflow>(
        CancellationToken cancel)
    {
        await using var env = await WorkflowEnvironment.StartLocalAsync();

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions($"test-{Guid.NewGuid():N}")
                .AddWorkflow<TWorkflow>()
                .AddWorkflow<AccountMonitorWorkflow>()
                .AddAllActivities(new OnboardingActivities())
                .AddAllActivities(new MonitorActivities()));

        return await worker.ExecuteAsync(
            async () =>
            {
                var workflowId = $"onboarding-{Guid.NewGuid():N}";

                var handle = await env.Client.StartWorkflowAsync(
                    "OnboardingWorkflow",
                    new object?[] { new OnboardingInput("acct-5", TimeSpan.Zero, Settings) },
                    new(id: workflowId, taskQueue: worker.Options.TaskQueue!));

                await handle.GetResultAsync();
                return await handle.FetchHistoryAsync();
            },
            cancel);
    }
}
