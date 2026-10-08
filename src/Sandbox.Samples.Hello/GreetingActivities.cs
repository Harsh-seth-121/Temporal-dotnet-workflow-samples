namespace Sandbox.Samples.Hello;

using Microsoft.Extensions.Logging;
using Temporalio.Activities;

/// <summary>
/// The one activity in the sandbox's starter sample. Activities are where
/// non-deterministic work belongs: network calls, file access, clocks, randomness.
/// </summary>
public class GreetingActivities
{
    [Activity]
    public string SayHello(string name)
    {
        ActivityExecutionContext.Current.Logger.LogInformation("Greeting {Name}", name);
        return $"Hello, {name}!";
    }
}
