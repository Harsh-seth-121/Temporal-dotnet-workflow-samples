// Starts one OnboardingWorkflow and prints where to find it and the monitor it leaves
// behind. This is what `make onboard` calls.
//
// Separate from Sandbox.Client rather than folded into it: that project is what
// `make run` and the smoke test both drive, and neither should grow a mode switch to
// make room for a second sample.
using System.Globalization;
using Sandbox.Abstractions;
using Sandbox.Samples.Onboarding;
using Temporalio.Client;

var config = SandboxConfig.FromEnvironment();

// Both arguments are always passed by the make target so a value containing spaces
// survives, which means a blank one arrives here when the variable was not set.
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
// Rate and per-cycle count are separate numbers and read as one if you put them in
// the same clause: "8 checks every 15s" says eight times the real rate. The seconds
// format is safe only because this client always sends MonitorSettings.Demo.
Console.WriteLine(
    $"The monitor checks every {settings.CheckInterval.TotalSeconds:0}s, {checks} checks " +
    $"a cycle, for {settings.MaxCycles} cycles, starting a fresh run each cycle. Watch it with:");
Console.WriteLine($"  temporal workflow describe --workflow-id {result.MonitorWorkflowId}");

static string? Argument(string[] given, int index) =>
    given.ElementAtOrDefault(index) is { } value && !string.IsNullOrWhiteSpace(value)
        ? value
        : null;

// Accepts what a person would type: 90s, 5m, 1h, or a bare number read as seconds.
// A value that parses as none of those stops the program rather than being silently
// dropped, because the whole reason to pass one is to hold the run open.
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
    // Invariant, not the current culture. The default overload treats '.' as a group
    // separator where the locale says so, which reads PAUSE=1.5m as fifteen minutes
    // instead of ninety seconds and says nothing about it. NumberStyles.Float is the
    // default set minus AllowThousands, so "90 s" still works and "1,000" now lands in
    // the error path rather than meaning two different things on two machines.
    if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var count)
        || count < 0)
    {
        Console.Error.WriteLine($"could not read PAUSE='{raw}'. Try 30s, 5m, 1h, or a number of seconds.");
        Environment.Exit(1);
    }

    return (scale == TimeSpan.Zero ? TimeSpan.FromSeconds(1) : scale) * count;
}
