namespace Sandbox.Samples.Onboarding.Tests;

using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

public class AccountMonitorWorkflowTests
{
    /// <summary>
    /// State survives every continue-as-new boundary.
    ///
    /// Runs on the time-skipping environment, so three hours of production-shaped
    /// timers cost no wall clock. Time skipping is not thread safe, so this test owns
    /// its environment rather than sharing one.
    ///
    /// The assertion is the running total. A monitor that restarted from zero at each
    /// boundary would still finish, still report the right number of cycles, and still
    /// look correct in the UI; only the total gives it away.
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

        // The production shape, bounded so it ends: a check every quarter hour, a new
        // run every hour, three runs.
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

    /// <summary>
    /// A continue-as-new keeps the workflow ID and changes only the run ID. That is
    /// what lets the parent name the child once and have the name stay true for the
    /// monitor's whole life, so it is asserted rather than assumed.
    /// </summary>
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

                // Fetched by ID with no run ID, so this is whatever run is current now.
                var current = await env.Client.GetWorkflowHandle(workflowId).DescribeAsync();

                Assert.Equal(workflowId, current.Id);
                Assert.NotEqual(firstRunId, current.RunId);
            },
            cancel);
    }

    /// <summary>
    /// A non-positive check interval fails the monitor immediately instead of spinning.
    ///
    /// Without the guard this is not a slow monitor, it is a runaway. Counting checks
    /// rather than timing them means a cycle lasts as long as its delays, so a zero
    /// interval gives a cycle no duration at all, and an unbounded monitor continues as
    /// new for as fast as the cluster will serve it. Measured before the guard existed:
    /// five nominal one-hour cycles finished in under a second.
    ///
    /// MaxCycles is 5 here rather than 0 deliberately. If the guard is ever removed this
    /// test must fail, not hang the suite forever.
    /// </summary>
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
