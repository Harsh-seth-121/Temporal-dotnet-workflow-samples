namespace Sandbox.Samples.Onboarding;

using Microsoft.Extensions.Logging;
using Temporalio.Activities;

/// <summary>
/// Provides account-health checks for <see cref="AccountMonitorWorkflow"/>.
/// </summary>
public class MonitorActivities
{
    [Activity]
    public string CheckAccountHealth(string accountId)
    {
        ActivityExecutionContext.Current.Logger.LogInformation("Checking {Account}", accountId);
        return $"{accountId} healthy";
    }
}
