using System.Globalization;
using Sandbox.Abstractions;
using Sandbox.Samples.Onboarding;
using Temporalio.Client;

var config = SandboxConfig.FromEnvironment();

var account = Argument(args, 0) ?? $"acct-{Guid.NewGuid():N}"[..13];
var holdOpenFor = ParsePause(Argument(args, 1));

var client = await TemporalClient.ConnectAsync(new(config.Address)
{
    Namespace = config.Namespace,
});

var workflowId = $"onboarding-{Guid.NewGuid():N}";

var result = await client.ExecuteWorkflowAsync(
    (OnboardingWorkflow wf) => wf.RunAsync(
        new OnboardingInput(account, holdOpenFor, MonitorSettings.Demo)),
    new(id: workflowId, taskQueue: config.TaskQueue));

var settings = MonitorSettings.Demo;
var checks = AccountMonitorWorkflow.ChecksPerCycle(settings);

Console.WriteLine($"account       {result.AccountId}");
Console.WriteLine($"onboarding    {workflowId}  (completed)");
Console.WriteLine($"welcomed by   {(result.WelcomedByEmail ? "email" : "letter")}");
Console.WriteLine($"monitor       {result.MonitorWorkflowId}  (still running)");
Console.WriteLine($"monitor run   {result.MonitorFirstRunId}");
Console.WriteLine();
Console.WriteLine(
    $"The monitor checks every {settings.CheckInterval.TotalSeconds:0}s, {checks} checks " +
    $"a cycle, for {settings.MaxCycles} cycles, starting a fresh run each cycle. Watch it with:");
Console.WriteLine($"  temporal workflow describe --workflow-id {result.MonitorWorkflowId}");

static string? Argument(string[] given, int index) =>
    given.ElementAtOrDefault(index) is { } value && !string.IsNullOrWhiteSpace(value)
        ? value
        : null;

// Accepts seconds by default or an s, m, or h suffix.
static TimeSpan ParsePause(string? raw)
{
    if (raw is null)
    {
        return TimeSpan.Zero;
    }

    var text = raw.Trim();
    var scale = text[^1] switch
    {
        's' or 'S' => TimeSpan.FromSeconds(1),
        'm' or 'M' => TimeSpan.FromMinutes(1),
        'h' or 'H' => TimeSpan.FromHours(1),
        _ => TimeSpan.Zero,
    };

    var number = scale == TimeSpan.Zero ? text : text[..^1];
    // Invariant culture and no thousands separators give each input one meaning on every host.
    if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var count)
        || count < 0)
    {
        Console.Error.WriteLine($"could not read PAUSE='{raw}'. Try 30s, 5m, 1h, or a number of seconds.");
        Environment.Exit(1);
    }

    return (scale == TimeSpan.Zero ? TimeSpan.FromSeconds(1) : scale) * count;
}
