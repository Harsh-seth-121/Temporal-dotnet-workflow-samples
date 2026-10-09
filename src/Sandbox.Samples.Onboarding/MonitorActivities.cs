namespace Sandbox.Samples.Onboarding;

using Microsoft.Extensions.Logging;
using Temporalio.Activities;

/// <summary>
/// The child's one step, run on a timer for as long as the monitor lives.
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
