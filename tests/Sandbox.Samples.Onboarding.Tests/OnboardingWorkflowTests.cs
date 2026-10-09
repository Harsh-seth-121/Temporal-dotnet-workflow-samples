namespace Sandbox.Samples.Onboarding.Tests;

using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

public class OnboardingWorkflowTests
{
    /// <summary>
    /// The monitor is named after its parent and is still running once the parent has
    /// finished.
    ///
    /// The second half is the one that earns its keep. ParentClosePolicy defaults to
    /// Terminate, so dropping the Abandon line leaves code that compiles, starts a
    /// child, completes, and quietly kills it. Nothing else in this repo would notice.
    /// </summary>
    [Fact]
    public async Task RunAsync_LeavesTheMonitorRunningUnderTheParentsId()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var env = await WorkflowEnvironment.StartLocalAsync();

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions($"test-{Guid.NewGuid():N}")
                .AddWorkflow<OnboardingWorkflow>()
                .AddWorkflow<AccountMonitorWorkflow>()
                .AddAllActivities(new OnboardingActivities())
                .AddAllActivities(new MonitorActivities()));

        // An hour between checks, so the monitor is parked on its first timer rather
        // than racing the assertions. Nothing waits that long: the parent does not
        // block on the child, and the test never advances the clock.
        var settings = new MonitorSettings(
            CheckInterval: TimeSpan.FromHours(1),
            CycleLength: TimeSpan.FromHours(8),
            MaxCycles: 2);

        await worker.ExecuteAsync(
            async () =>
            {
                var workflowId = $"onboarding-{Guid.NewGuid():N}";

                var result = await env.Client.ExecuteWorkflowAsync(
                    (OnboardingWorkflow wf) => wf.RunAsync(
                        new OnboardingInput("acct-1", TimeSpan.Zero, settings)),
                    new(id: workflowId, taskQueue: worker.Options.TaskQueue!));

                Assert.Equal($"{workflowId}-monitor", result.MonitorWorkflowId);
                Assert.NotEmpty(result.MonitorFirstRunId);

                var parent = await env.Client.GetWorkflowHandle(workflowId).DescribeAsync();
                Assert.Equal(WorkflowExecutionStatus.Completed, parent.Status);

                var monitor = await env.Client
                    .GetWorkflowHandle(result.MonitorWorkflowId).DescribeAsync();
                Assert.Equal(WorkflowExecutionStatus.Running, monitor.Status);
            },
            cancel);
    }

    /// <summary>
    /// A fresh run takes the patched path, because Patched returns true when there is
    /// no history telling it otherwise. This is the easy half of the patch; the hard
    /// half is in OnboardingPatchReplayTests.
    /// </summary>
    [Fact]
    public async Task RunAsync_WelcomesANewRunByEmail()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var env = await WorkflowEnvironment.StartLocalAsync();

        // Both arms are counted, not just the one that should stay at zero. Asserting
        // only the absence of the letter would pass against a workflow that took the
        // patched branch and then called nothing at all, which is what deleting the
        // SendWelcomeEmail line leaves behind. Each counter is named after the activity
        // it stands in for, so the workflow cannot reach the real one instead.
        var emails = 0;
        var letters = 0;

        [Temporalio.Activities.Activity("SendWelcomeEmail")]
        string CountEmail(string accountId)
        {
            emails++;
            return accountId;
        }

        [Temporalio.Activities.Activity("QueueWelcomeLetter")]
        string CountLetter(string accountId)
        {
            letters++;
            return accountId;
        }

        // Registered one by one rather than with AddAllActivities, because each counter
        // shares a name with the activity it replaces and the worker refuses a
        // duplicate. The first three are the real thing; only the branch is swapped.
        var real = new OnboardingActivities();

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions($"test-{Guid.NewGuid():N}")
                .AddWorkflow<OnboardingWorkflow>()
                .AddWorkflow<AccountMonitorWorkflow>()
                .AddActivity(real.ValidateAccount)
                .AddActivity(real.ScreenAccount)
                .AddActivity(real.ProvisionAccount)
                .AddActivity(CountEmail)
                .AddActivity(CountLetter)
                .AddAllActivities(new MonitorActivities()));

        var settings = new MonitorSettings(
            CheckInterval: TimeSpan.FromHours(1),
            CycleLength: TimeSpan.FromHours(8),
            MaxCycles: 1);

        await worker.ExecuteAsync(
            async () =>
            {
                var result = await env.Client.ExecuteWorkflowAsync(
                    (OnboardingWorkflow wf) => wf.RunAsync(
                        new OnboardingInput("acct-2", TimeSpan.Zero, settings)),
                    new(id: $"onboarding-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

                Assert.True(result.WelcomedByEmail);
                Assert.Equal(1, emails);
                Assert.Equal(0, letters);
            },
            cancel);
    }
}
