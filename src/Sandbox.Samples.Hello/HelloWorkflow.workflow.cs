namespace Sandbox.Samples.Hello;

using Temporalio.Workflows;

/// <summary>
/// Smallest useful workflow: one activity call.
///
/// The signature takes a single string on purpose. Load generators that drive an
/// arbitrary workflow type pass one JSON argument on the command line, so keeping
/// the input shape simple means the sandbox can be load tested without a shim.
/// </summary>
[Workflow]
public class HelloWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string name) =>
        await Workflow.ExecuteActivityAsync(
            (GreetingActivities act) => act.SayHello(name),
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
}
