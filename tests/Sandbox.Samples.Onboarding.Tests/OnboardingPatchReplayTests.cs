namespace Sandbox.Samples.Onboarding.Tests;

using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

public class OnboardingPatchReplayTests
{
    // Keeps the abandoned monitor idle until fixture teardown.
    private static readonly MonitorSettings Settings = new(
        CheckInterval: TimeSpan.FromHours(1),
        CycleLength: TimeSpan.FromHours(1),
        MaxCycles: 1);

    /// <summary>
    /// Pre-email-patch history remains compatible with both current patch markers.
    /// </summary>
    [Fact]
    public async Task PrePatchHistoryStillReplaysAgainstTodaysWorkflow()
    {
        var cancel = TestContext.Current.CancellationToken;
        var history = await RecordAsync<OnboardingWorkflowBeforeEmailPatch>(cancel);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions().AddWorkflow<OnboardingWorkflow>());

        await replayer.ReplayWorkflowAsync(history, cancellationToken: cancel);
    }

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
