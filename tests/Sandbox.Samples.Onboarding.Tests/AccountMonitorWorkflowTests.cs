namespace Sandbox.Samples.Onboarding.Tests;

using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

public class AccountMonitorWorkflowTests
{
    /// <summary>
    /// Totals survive every continue-as-new boundary.
    /// </summary>
    [Fact]
    public async Task RunAsync_CarriesItsTotalsAcrossEveryCycle()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions($"test-{Guid.NewGuid():N}")
                .AddWorkflow<AccountMonitorWorkflow>()
                .AddAllActivities(new MonitorActivities()));

        var settings = new MonitorSettings(
            CheckInterval: TimeSpan.FromMinutes(15),
            CycleLength: TimeSpan.FromHours(1),
            MaxCycles: 3);

        var perCycle = AccountMonitorWorkflow.ChecksPerCycle(settings);
        Assert.Equal(4, perCycle);

        await worker.ExecuteAsync(
            async () =>
            {
                var result = await env.Client.ExecuteWorkflowAsync(
                    (AccountMonitorWorkflow wf) => wf.RunAsync(
                        new MonitorState("acct-3", settings, Cycle: 0, ChecksDone: 0)),
                    new(id: $"monitor-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

                Assert.Equal(3, result.Cycles);
                Assert.Equal(perCycle * 3, result.ChecksDone);
                Assert.Equal("acct-3", result.AccountId);
            },
            cancel);
    }

    [Fact]
    public async Task RunAsync_KeepsItsWorkflowIdAndChangesOnlyTheRunId()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions($"test-{Guid.NewGuid():N}")
                .AddWorkflow<AccountMonitorWorkflow>()
                .AddAllActivities(new MonitorActivities()));

        var settings = new MonitorSettings(
            CheckInterval: TimeSpan.FromMinutes(15),
            CycleLength: TimeSpan.FromHours(1),
            MaxCycles: 2);

        await worker.ExecuteAsync(
            async () =>
            {
                var workflowId = $"monitor-{Guid.NewGuid():N}";

                var handle = await env.Client.StartWorkflowAsync(
                    (AccountMonitorWorkflow wf) => wf.RunAsync(
                        new MonitorState("acct-4", settings, Cycle: 0, ChecksDone: 0)),
                    new(id: workflowId, taskQueue: worker.Options.TaskQueue!));

                var firstRunId = handle.ResultRunId;
                await handle.GetResultAsync();

                // A handle without a run ID resolves to the current run.
                var current = await env.Client.GetWorkflowHandle(workflowId).DescribeAsync();

                Assert.Equal(workflowId, current.Id);
                Assert.NotEqual(firstRunId, current.RunId);
            },
            cancel);
    }

    [Fact]
    public async Task RunAsync_RejectsANonPositiveCheckInterval()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var env = await WorkflowEnvironment.StartLocalAsync();

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions($"test-{Guid.NewGuid():N}")
                .AddWorkflow<AccountMonitorWorkflow>()
                .AddAllActivities(new MonitorActivities()));

        // A finite cycle limit prevents a regression from running indefinitely.
        var settings = new MonitorSettings(
            CheckInterval: TimeSpan.Zero,
            CycleLength: TimeSpan.FromHours(1),
            MaxCycles: 5);

        await worker.ExecuteAsync(
            async () =>
            {
                var failure = await Assert.ThrowsAsync<WorkflowFailedException>(() =>
                    env.Client.ExecuteWorkflowAsync(
                        (AccountMonitorWorkflow wf) => wf.RunAsync(
                            new MonitorState("acct-5", settings, Cycle: 0, ChecksDone: 0)),
                        new(id: $"monitor-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!)));

                var cause = Assert.IsType<ApplicationFailureException>(failure.InnerException);
                Assert.Contains("CheckInterval must be positive", cause.Message);
                Assert.True(cause.NonRetryable);
            },
            cancel);
    }
}
