namespace Sandbox.Samples.Onboarding;

using Microsoft.Extensions.Logging;
using Temporalio.Activities;

/// <summary>
/// The parent's steps. Each one logs and returns; nothing here talks to a real
/// system, because the point of the sample is the shape of the workflow around them.
///
/// There are two pairs worth knowing about. <see cref="ScreenAccount"/> is the step a
/// patch introduced, and <see cref="SendWelcomeEmail"/> replaced
/// <see cref="QueueWelcomeLetter"/> under a second one. The workflow is where those
/// decisions live; see OnboardingWorkflow.workflow.cs.
/// </summary>
public class OnboardingActivities
{
    [Activity]
    public string ValidateAccount(string accountId)
    {
        ActivityExecutionContext.Current.Logger.LogInformation("Validating {Account}", accountId);
        return $"{accountId} validated";
    }

    [Activity]
    public string ScreenAccount(string accountId)
    {
        ActivityExecutionContext.Current.Logger.LogInformation("Screening {Account}", accountId);
        return $"{accountId} screened";
    }

    [Activity]
    public string ProvisionAccount(string accountId)
    {
        ActivityExecutionContext.Current.Logger.LogInformation("Provisioning {Account}", accountId);
        return $"{accountId} provisioned";
    }

    [Activity]
    public string SendWelcomeEmail(string accountId)
    {
        ActivityExecutionContext.Current.Logger.LogInformation("Emailing a welcome to {Account}", accountId);
        return $"{accountId} emailed";
    }

    [Activity]
    public string QueueWelcomeLetter(string accountId)
    {
        ActivityExecutionContext.Current.Logger.LogInformation("Queueing a welcome letter for {Account}", accountId);
        return $"{accountId} sent to the print queue";
    }
}
