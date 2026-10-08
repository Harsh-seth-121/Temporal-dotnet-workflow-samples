namespace Sandbox.Samples.Hello;

using Sandbox.Abstractions;
using Temporalio.Worker;

/// <summary>
/// Wires this sample into the shared worker. Discovered by reflection at startup.
/// </summary>
public class HelloSampleModule : ISampleModule
{
    public string Name => "hello";

    public void Register(TemporalWorkerOptions options, IServiceProvider services)
    {
        options.AddWorkflow<HelloWorkflow>();

        // AddAllActivities has no parameterless overload: activity instance methods
        // need a real instance to invoke against.
        options.AddAllActivities(new GreetingActivities());
    }
}
