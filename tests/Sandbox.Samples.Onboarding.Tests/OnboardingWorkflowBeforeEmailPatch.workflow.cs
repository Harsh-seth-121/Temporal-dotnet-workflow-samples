namespace Sandbox.Samples.Onboarding.Tests;

using Temporalio.Workflows;

/// <summary>
/// OnboardingWorkflow exactly as it stood before the welcome-email patch was added.
/// It exists only to produce a history that predates that patch, which
/// OnboardingPatchReplayTests then replays against the real workflow.
///
/// It registers under the production workflow type name, so the history it writes is
/// indistinguishable from one a real pre-patch worker would have written. That is the
/// whole point: a hand-built history would only prove that a hand-built history
/// replays.
///
/// It lives in the test project, and SampleDiscovery skips any assembly ending
/// ".Tests", so it can never reach the real worker and collide with the class whose
/// name it borrows.
///
/// Note what this copy does and does not contain. The screening patch is here, in its
/// `if` form, because that is where the code genuinely was: patch one had been added
/// and had not yet been deprecated. A copy predating *both* patches would issue a
/// different command sequence and would fail to replay against today's
/// DeprecatePatch call, which is precisely why you do not deprecate until every
/// pre-patch run has finished.
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

        // Same position as the live workflow's, so a history recorded here parks at the
        // same point and the replay below compares like with like.
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
