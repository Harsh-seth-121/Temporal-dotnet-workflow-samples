namespace Sandbox.Samples.Onboarding.Tests;

using Temporalio.Workflows;

/// <summary>
/// Produces history with the screening patch but without the welcome-email patch.
/// </summary>
[Workflow("OnboardingWorkflow")]
public class OnboardingWorkflowBeforeEmailPatch
{
    [WorkflowRun]
    public async Task<OnboardingResult> RunAsync(OnboardingInput input)
    {
        var activity = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) };

        await Workflow.ExecuteActivityAsync(
            (OnboardingActivities act) => act.ValidateAccount(input.AccountId), activity);

        if (Workflow.Patched("screen-before-provision"))
        {
            await Workflow.ExecuteActivityAsync(
                (OnboardingActivities act) => act.ScreenAccount(input.AccountId), activity);
        }

        await Workflow.ExecuteActivityAsync(
            (OnboardingActivities act) => act.ProvisionAccount(input.AccountId), activity);

        await Workflow.ExecuteActivityAsync(
            (OnboardingActivities act) => act.QueueWelcomeLetter(input.AccountId), activity);

        // Timer position must match the live workflow's command order for replay.
        if (input.HoldOpenFor > TimeSpan.Zero)
        {
            await Workflow.DelayAsync(input.HoldOpenFor);
        }

        var monitor = await Workflow.StartChildWorkflowAsync(
            (AccountMonitorWorkflow wf) => wf.RunAsync(
                new MonitorState(input.AccountId, input.Monitor, Cycle: 0, ChecksDone: 0)),
            new ChildWorkflowOptions
            {
                Id = $"{Workflow.Info.WorkflowId}-monitor",
                ParentClosePolicy = ParentClosePolicy.Abandon,
            });

        return new OnboardingResult(
            input.AccountId,
            WelcomedByEmail: false,
            monitor.Id,
            monitor.FirstExecutionRunId);
    }
}
