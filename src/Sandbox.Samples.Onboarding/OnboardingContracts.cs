namespace Sandbox.Samples.Onboarding;

/// <summary>
/// Input for <see cref="OnboardingWorkflow"/>.
/// </summary>
/// <param name="HoldOpenFor">Duration of the post-welcome pause; non-positive values disable it.</param>
public sealed record OnboardingInput(
    string AccountId,
    TimeSpan HoldOpenFor,
    MonitorSettings Monitor);

/// <summary>
/// Result of onboarding, including identifiers for the detached monitor workflow.
/// </summary>
public sealed record OnboardingResult(
    string AccountId,
    bool WelcomedByEmail,
    string MonitorWorkflowId,
    string MonitorFirstRunId);

/// <summary>
/// Scheduling and cycle limits for account monitoring.
/// </summary>
/// <param name="MaxCycles">Maximum cycle count; zero runs until externally stopped.</param>
public sealed record MonitorSettings(
    TimeSpan CheckInterval,
    TimeSpan CycleLength,
    int MaxCycles)
{
    public static MonitorSettings Demo { get; } =
        new(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2), MaxCycles: 3);

    public static MonitorSettings Production { get; } =
        new(TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), MaxCycles: 0);
}

/// <summary>
/// State carried across continue-as-new runs, with <paramref name="ChecksDone"/>
/// cumulative over the workflow execution.
/// </summary>
public sealed record MonitorState(
    string AccountId,
    MonitorSettings Settings,
    int Cycle,
    int ChecksDone);

/// <summary>Final account-monitoring totals.</summary>
public sealed record MonitorResult(
    string AccountId,
    int ChecksDone,
    int Cycles);
