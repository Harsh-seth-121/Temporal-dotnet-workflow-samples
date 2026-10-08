namespace Sandbox.Samples.Hello.Tests;

using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

public class HelloWorkflowTests
{
    [Fact]
    public async Task RunAsync_CallsActivityAndReturnsGreeting()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var env = await WorkflowEnvironment.StartLocalAsync();

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions($"test-{Guid.NewGuid():N}")
                .AddWorkflow<HelloWorkflow>()
                .AddAllActivities(new GreetingActivities()));

        await worker.ExecuteAsync(
            async () =>
            {
                var result = await env.Client.ExecuteWorkflowAsync(
                    (HelloWorkflow wf) => wf.RunAsync("Temporal"),
                    new(id: $"wf-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

                Assert.Equal("Hello, Temporal!", result);
            },
            cancel);
    }

    [Fact]
    public async Task RunAsync_GoesThroughTheActivityRatherThanComputingTheGreeting()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var env = await WorkflowEnvironment.StartLocalAsync();

        // A local function carrying the original activity's name replaces it. If the
        // workflow ever stopped calling the activity, this test would still see the
        // real greeting and fail.
        [Temporalio.Activities.Activity("SayHello")]
        static string MockSayHello(string name) => $"mocked greeting for {name}";

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions($"test-{Guid.NewGuid():N}")
                .AddWorkflow<HelloWorkflow>()
                .AddActivity(MockSayHello));

        await worker.ExecuteAsync(
            async () =>
            {
                var result = await env.Client.ExecuteWorkflowAsync(
                    (HelloWorkflow wf) => wf.RunAsync("Temporal"),
                    new(id: $"wf-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

                Assert.Equal("mocked greeting for Temporal", result);
            },
            cancel);
    }
}
