namespace Sandbox.Samples.Onboarding;

using Microsoft.Extensions.Logging;
using Temporalio.Activities;

/// <summary>
/// Provides account setup activities for <see cref="OnboardingWorkflow"/>.
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
