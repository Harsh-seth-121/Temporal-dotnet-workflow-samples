// Starts one HelloWorkflow and prints its result. This is what `make run` calls,
// and what the smoke test asserts against.
using Sandbox.Abstractions;
using Sandbox.Samples.Hello;
using Temporalio.Client;

var config = SandboxConfig.FromEnvironment();

// `make run` always passes an argument so a name containing spaces survives, which
// means a blank one arrives here when no NAME was given.
var name = args.FirstOrDefault() is { } given && !string.IsNullOrWhiteSpace(given)
    ? given
    : "World";

var client = await TemporalClient.ConnectAsync(new(config.Address)
{
    Namespace = config.Namespace,
});

var result = await client.ExecuteWorkflowAsync(
    (HelloWorkflow wf) => wf.RunAsync(name),
    new(id: $"hello-{Guid.NewGuid():N}", taskQueue: config.TaskQueue));

Console.WriteLine(result);
