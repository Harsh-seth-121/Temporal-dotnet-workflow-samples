namespace Sandbox.Samples.Onboarding.Tests;

using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

public class OnboardingWorkflowTests
{
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

        // Keeps the monitor running without racing the status assertion.
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

    [Fact]
    public async Task RunAsync_WelcomesANewRunByEmail()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var env = await WorkflowEnvironment.StartLocalAsync();

        // Counts both branch activities to detect a missing email or unexpected letter.
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
