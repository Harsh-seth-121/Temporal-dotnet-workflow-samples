namespace Sandbox.Samples.Onboarding;

using Temporalio.Workflows;

/// <summary>
/// Sets an account up, then hands it to a monitor that outlives this workflow.
///
/// Two things are on show here, and they are the two that break real deployments.
///
/// The first is patching. A worker that restarts replays every open workflow from
/// the top, and if the new code issues different commands than the history records,
/// the run wedges with a non-determinism error. Patching is how you change a
/// workflow anyway. This file holds two patches, deliberately left at different
/// points in their lifecycle, so the order is visible in one read:
///
///   1. patch in   if (Workflow.Patched(id)) { new } else { old }
///   2. deprecate  Workflow.DeprecatePatch(id);  only the new path remains
///   3. remove     the call goes away entirely
///
/// You only move from 1 to 2 once no pre-patch run is still open, and from 2 to 3
/// once no run carrying the marker is still open. Moving early is not a style
/// mistake, it strands live workflows. The two list queries that answer those two
/// questions are in the README, and they are not the same query: a run that predates
/// one patch still carries the other one's marker, so "has no markers" is the wrong
/// test here.
///
/// The second is handing work off. The monitor at the bottom runs for hours; this
/// workflow finishes in seconds and must not keep it waiting or take it down with
/// it. See the comments on that call.
/// </summary>
[Workflow]
public class OnboardingWorkflow
{
    /// <summary>
    /// Phase two of three. Screening was added by a patch; every run that predates it
    /// has since finished, so the old no-screening path is gone and this call is all
    /// that remains. It is still here because histories recorded while the patch was
    /// live carry its marker, and the SDK has to be told that marker is expected.
    /// Deleting this line is phase three, and it is safe only once no run carrying
    /// the marker is open.
    /// </summary>
    private const string ScreeningPatch = "screen-before-provision";

    /// <summary>
    /// Phase one of three. Both paths are live: new runs email, runs that started
    /// before this patch existed keep queueing a letter all the way to completion.
    /// The else branch looks like dead code on a healthy box and is not; it is the
    /// only thing standing between an in-flight run and a wedged workflow.
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

        // Read once into a local. Calling Patched twice would be correct but reads as
        // though the answer could change mid-run, and the result is reported below.
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

        // The one timer in this workflow, and only when asked for. It sits here, after
        // the branch, because this is the only point worth parking a run at: its
        // history now records which way the patch went, which is the state the patch
        // has to read on the way back in. Parked any earlier the run has finished
        // replaying before it reaches Patched, so the answer is always yes and the
        // else branch is unreachable. The README walkthrough depends on that.
        await PauseAsync(input.HoldOpenFor);

        // StartChildWorkflowAsync, not ExecuteChildWorkflowAsync. Execute waits for the
        // child to finish, which here would mean sitting on this line for hours. Start
        // returns as soon as the child has actually started, which is still a real
        // guarantee: the handle below cannot exist unless the server recorded it.
        //
        // ParentClosePolicy carries the rest of the weight. It defaults to Terminate,
        // so without this line the monitor would be killed the instant this workflow
        // returned, and the only symptom would be a monitor that is somehow never
        // running. Abandon cuts the tie: the child keeps going on its own.
        //
        // The child's ID is this workflow's ID with a suffix, so either one finds the
        // other in the UI or in a list query. Continue-as-new changes a run ID and
        // never the workflow ID, so that stays true for the monitor's whole life.
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

    /// <summary>
    /// A timer, but only when one was asked for. Zero is the normal case and records
    /// nothing, which keeps an ordinary history short. The branch reads an input
    /// value, fixed in history, so it decides the same way on every replay.
    /// </summary>
    private static Task PauseAsync(TimeSpan pause) =>
        pause > TimeSpan.Zero ? Workflow.DelayAsync(pause) : Task.CompletedTask;
}
