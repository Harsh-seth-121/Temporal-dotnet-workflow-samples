namespace Sandbox.Samples.Onboarding;

using Temporalio.Workflows;

/// <summary>
/// Sets up an account and starts an independent monitor that outlives onboarding.
/// </summary>
[Workflow]
public class OnboardingWorkflow
{
    /// <summary>
    /// Identifies the deprecated screening patch retained while open workflows may carry its marker.
    /// </summary>
    private const string ScreeningPatch = "screen-before-provision";

    /// <summary>
    /// Identifies the welcome-delivery patch that preserves letter delivery when replaying pre-patch histories.
    /// </summary>
    private const string EmailPatch = "email-welcome-instead-of-letter";

    [WorkflowRun]
    public async Task<OnboardingResult> RunAsync(OnboardingInput input)
    {
        var activity = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) };

        await Workflow.ExecuteActivityAsync(
            (OnboardingActivities act) => act.ValidateAccount(input.AccountId), activity);

        Workflow.DeprecatePatch(ScreeningPatch);
        await Workflow.ExecuteActivityAsync(
            (OnboardingActivities act) => act.ScreenAccount(input.AccountId), activity);

        await Workflow.ExecuteActivityAsync(
            (OnboardingActivities act) => act.ProvisionAccount(input.AccountId), activity);

        var welcomedByEmail = Workflow.Patched(EmailPatch);
        if (welcomedByEmail)
        {
            await Workflow.ExecuteActivityAsync(
                (OnboardingActivities act) => act.SendWelcomeEmail(input.AccountId), activity);
        }
        else
        {
            await Workflow.ExecuteActivityAsync(
                (OnboardingActivities act) => act.QueueWelcomeLetter(input.AccountId), activity);
        }

        // Held-open histories include the welcome patch decision because the pause follows the branch.
        await PauseAsync(input.HoldOpenFor);

        // The monitor starts independently under Abandon so it outlives onboarding.
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
            welcomedByEmail,
            monitor.Id,
            monitor.FirstExecutionRunId);
    }

    private static Task PauseAsync(TimeSpan pause) =>
        pause > TimeSpan.Zero ? Workflow.DelayAsync(pause) : Task.CompletedTask;
}
